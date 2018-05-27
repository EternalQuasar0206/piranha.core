﻿/*
 * Copyright (c) 2016-2018 Håkan Edling
 *
 * This software may be modified and distributed under the terms
 * of the MIT license.  See the LICENSE file for details.
 * 
 * https://github.com/piranhacms/piranha.core
 * 
 */

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Piranha.Web;
using System;
using System.Threading.Tasks;

namespace Piranha.AspNetCore
{
    public class PageMiddleware : MiddlewareBase
    {
        /// <summary>
        /// Creates a new middleware instance.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline</param>
        /// <param name="factory">The logger factory</param>
        public PageMiddleware(RequestDelegate next, ILoggerFactory factory = null) : base(next, factory) { }

        /// <summary>
        /// Invokes the middleware.
        /// </summary>
        /// <param name="context">The current http context</param>
        /// <param name="api">The current api</param>
        /// <returns>An async task</returns>
        public override async Task Invoke(HttpContext context, IApi api)
        {
            if (!IsHandled(context) && !context.Request.Path.Value.StartsWith("/manager/assets/"))
            {
                var url = context.Request.Path.HasValue ? context.Request.Path.Value : "";
                var siteId = GetSiteId(context);
                var authorized = true;

                var response = PageRouter.Invoke(api, url, siteId);
                if (response != null)
                {
                    if (_logger != null)
                        _logger.LogInformation($"Found page\n  Route: {response.Route}\n  Params: {response.QueryString}");

                    if (!response.IsPublished)
                    {
                        if (!context.User.HasClaim(Security.Permission.PagePreview, Security.Permission.PagePreview))
                        {
                            if (_logger != null)
                                _logger.LogInformation($"User not authorized to preview unpublished page");
                            authorized = false;
                        }
                    }

                    if (authorized)
                    {
                        if (string.IsNullOrWhiteSpace(response.RedirectUrl))
                        {
                            using (var config = new Config(api))
                            {
                                var headers = context.Response.GetTypedHeaders();
                                var expires = config.CacheExpiresPages;

                                // Only use caching for published pages
                                if (response.IsPublished && expires > 0)
                                {
                                    if (_logger != null)
                                        _logger.LogInformation("Caching enabled. Setting MaxAge, LastModified & ETag");

                                    headers.CacheControl = new CacheControlHeaderValue
                                    {
                                        Public = true,
                                        MaxAge = TimeSpan.FromMinutes(expires),
                                    };

                                    headers.Headers["ETag"] = response.CacheInfo.EntityTag;
                                    headers.LastModified = response.CacheInfo.LastModified;
                                }
                                else
                                {
                                    headers.CacheControl = new CacheControlHeaderValue
                                    {
                                        NoCache = true
                                    };
                                }
                            }

                            if (HttpCaching.IsCached(context, response.CacheInfo))
                            {
                                if (_logger != null)
                                    _logger.LogInformation("Client has current version. Returning NotModified");

                                context.Response.StatusCode = 304;
                                return;
                            }
                            else
                            {
                                context.Request.Path = new PathString(response.Route);

                                if (context.Request.QueryString.HasValue)
                                {
                                    context.Request.QueryString = new QueryString(context.Request.QueryString.Value + "&" + response.QueryString);
                                }
                                else context.Request.QueryString = new QueryString("?" + response.QueryString);
                            }
                        }
                        else
                        {
                            if (_logger != null)
                                _logger.LogInformation($"Redirecting to url: {response.RedirectUrl}");

                            context.Response.Redirect(response.RedirectUrl, response.RedirectType == Models.RedirectType.Permanent);
                            return;
                        }
                    }
                }
            }
            await _next.Invoke(context);
        }
    }
}
