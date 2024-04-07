// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Azure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Microsoft.Health.Fhir.Web
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // The initial "bootstrap" logger is able to log errors during start-up. It's completely replaced by the
            // logger configured in `AddSerilog()` below, once configuration and dependency-injection have both been
            // set up successfully.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            Log.Information("Starting up!");

            try
            {
                var webApplicationOptions = new WebApplicationOptions()
                {
                    Args = args,
                    ContentRootPath = Path.GetDirectoryName(typeof(Program).Assembly.Location),
                };

                var builder = WebApplication.CreateBuilder(webApplicationOptions);

                builder.Services.AddSerilog((services, lc) => lc
                    .ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(new ExpressionTemplate(
                        "[{@t:HH:mm:ss} {@l:u3}{#if @tr is not null} ({substring(@tr,0,4)}:{substring(@sp,0,4)}){#end}] {@m}\n{@x}",
                        theme: TemplateTheme.Code)));

                builder.Configuration.Sources.Add(new GenericConfigurationSource(() => new DictionaryExpansionConfigurationProvider(new EnvironmentVariablesConfigurationProvider())));

                builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

                builder.Configuration.AddDevelopmentAuthEnvironmentIfConfigured(builder.Configuration);

                Startup startup = new Startup(builder.Configuration);

                startup.ConfigureServices(builder.Services);

                var app = builder.Build();

                // Write streamlined request completion events, instead of the more verbose ones from the framework.
                // To use the default framework request logging instead, remove this line and set the "Microsoft"
                // level in appsettings.json to "Information".
                // app.UseSerilogRequestLogging();
                app.UseSerilogRequestLogging(options =>
                {
                    // Customize the message template
                    options.MessageTemplate = "Handled {RequestPath}";

                    // Emit debug-level events instead of the defaults
                    options.GetLevel = (httpContext, elapsed, ex) => LogEventLevel.Debug;

                    // Attach additional properties to the request completion event
                    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                    {
                        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                    };
                });
                startup.Configure(app);

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unhandled exception occurred during bootstrapping");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }

            return 0;
        }

        /*
        public static void Main(string[] args)
        {
            var host = WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location))
                .ConfigureAppConfiguration((hostContext, builder) =>
                {
                    builder.Sources.Add(new GenericConfigurationSource(() => new DictionaryExpansionConfigurationProvider(new EnvironmentVariablesConfigurationProvider())));

                    var builtConfig = builder.Build();

                    var keyVaultEndpoint = builtConfig["KeyVault:Endpoint"];
                    if (!string.IsNullOrEmpty(keyVaultEndpoint))
                    {
                        var credential = new DefaultAzureCredential();
                        builder.AddAzureKeyVault(new System.Uri(keyVaultEndpoint), credential);
                    }

                    builder.AddDevelopmentAuthEnvironmentIfConfigured(builtConfig);
                })
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
        */
    }
}
