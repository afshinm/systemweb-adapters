// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Web;
using System.Web.Caching;
using System.Web.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SystemWebAdapters;

namespace Microsoft.Extensions.DependencyInjection;

public static class SystemWebAdaptersExtensions
{
    public static ISystemWebAdapterBuilder AddSystemWebAdapters(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IHttpRuntime>(sp => HttpRuntimeFactory.Create(sp));
        services.AddSingleton<Cache>();
        services.AddSingleton<IBrowserCapabilitiesFactory, BrowserCapabilitiesFactory>();
        services.AddTransient<IStartupFilter, HttpContextStartupFilter>();

        return new SystemWebAdapterBuilder(services)
            .AddMvc();
    }

    internal static bool HasBeenAdded(this IApplicationBuilder app, [CallerMemberName] string key = null!)
    {
        if (app.Properties.ContainsKey(key))
        {
            return true;
        }

        app.Properties[key] = true;
        return false;
    }

    internal static void UseSystemWebAdapterFeatures(this IApplicationBuilder app)
    {
        if (app.HasBeenAdded())
        {
            return;
        }

        app.UseMiddleware<RegisterAdapterFeaturesMiddleware>();
        app.UseMiddleware<PreBufferRequestStreamMiddleware>();
        app.UseMiddleware<BufferResponseStreamMiddleware>();

        app.UseMiddleware<SetDefaultResponseHeadersMiddleware>();
        app.UseMiddleware<SingleThreadedRequestMiddleware>();
        app.UseMiddleware<CurrentPrincipalMiddleware>();

        app.UseHttpApplication();
    }

    public static void UseSystemWebAdapters(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        HttpRuntime.Current = app.ApplicationServices.GetRequiredService<IHttpRuntime>();

        app.UseSystemWebAdapterFeatures();
        app.UseAuthenticationEvents();
        app.UseAuthorizationEvents();

        app.UseHttpApplicationEvent(
            preEvents: new[]
            {
                ApplicationEvent.ResolveRequestCache,
                ApplicationEvent.PostResolveRequestCache,
                ApplicationEvent.MapRequestHandler,
                ApplicationEvent.PostMapRequestHandler,
                ApplicationEvent.AcquireRequestState,
                ApplicationEvent.PostAcquireRequestState,
            },
            postEvents: new[]
            {
                ApplicationEvent.ReleaseRequestState,
                ApplicationEvent.PostReleaseRequestState,
                ApplicationEvent.UpdateRequestCache,
                ApplicationEvent.PostUpdateRequestCache,
            });

        app.UseMiddleware<SessionMiddleware>();

        if (app.AreHttpApplicationEventsRequired())
        {
            app.UseMiddleware<SessionEventsMiddleware>();
        }

        app.UseHttpApplicationEvent(
            preEvents: new[] { ApplicationEvent.PreRequestHandlerExecute },
            postEvents: new[] { ApplicationEvent.PostRequestHandlerExecute });
    }

    /// <summary>
    /// Adds request stream buffering to the endpoint(s)
    /// </summary>
    public static TBuilder PreBufferRequestStream<TBuilder>(this TBuilder builder, PreBufferRequestStreamAttribute? metadata = null)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(metadata ?? new PreBufferRequestStreamAttribute());

    /// <summary>
    /// Adds session support for System.Web adapters for the endpoint(s)
    /// </summary>
    public static TBuilder RequireSystemWebAdapterSession<TBuilder>(this TBuilder builder, SessionAttribute? metadata = null)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(metadata ?? new SessionAttribute());

    /// <summary>
    /// Ensure response stream is buffered to enable synchronous actions on it for the endpoint(s)
    /// </summary>
    public static TBuilder BufferResponseStream<TBuilder>(this TBuilder builder, BufferResponseStreamAttribute? metadata = null)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(metadata ?? new BufferResponseStreamAttribute());

    internal sealed class HttpContextStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => builder =>
            {
                builder.UseMiddleware<SetHttpContextTimestampMiddleware>();

                next(builder);
            };
    }
}
