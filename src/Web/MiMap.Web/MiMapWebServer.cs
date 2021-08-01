using System;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MiMap.Common.Configuration;
using MiMap.Web.Sockets;
using MiMap.Web.Utils;
using MiMap.Web.Widgets;
using WebSocketProxy;
using Host = WebSocketProxy.Host;

namespace MiMap.Web
{
    public class MiMapWebServer : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MiMapWebServer));

        private          WidgetManager  WidgetManager { get; }
        private readonly TcpProxyServer _proxy;
        private          IHost          _host;

        private short PublicPort           { get; }
        private short PrivateWebPort       { get; }
        private short PrivateWebSocketPort { get; }

        public string[] Urls { get; }

        public MiMapWebServer() : this(new MiMapWebServerConfig())
        {
        }

        public MiMapWebServer(MiMapWebServerConfig config) : this(config.Port, config.InternalWebPort, config.InternalWebSocketPort, config.Widgets)
        {
        }

        public MiMapWebServer(short publicPort, MiMapWidgetConfig[] widgets) : this(publicPort, (short) (publicPort + 1), (short) (publicPort + 2), widgets)
        {
        }

        public MiMapWebServer(short publicPort, short privateWebPort, short privateWebSocketPort, MiMapWidgetConfig[] widgets)
        {
            PublicPort = publicPort;
            PrivateWebPort = privateWebPort;
            PrivateWebSocketPort = privateWebSocketPort;

            Urls = new[] {$"http://+:{PrivateWebPort}"};

            _proxy = new TcpProxyServer(new TcpProxyConfiguration()
            {
                PublicHost = new Host()
                {
                    IpAddress = IPAddress.IPv6Any,
                    Port = PublicPort
                },
                HttpHost = new Host()
                {
                    IpAddress = IPAddress.IPv6Loopback,
                    Port = PrivateWebPort
                },
                WebSocketHost = new Host()
                {
                    IpAddress = IPAddress.Loopback,
                    Port = PrivateWebSocketPort
                }
            });

            WidgetManager = new WidgetManager(widgets);

            /*
            _host = new NancyHost(new Uri("http://localhost:8125"), new WebBootstrapper(), new HostConfiguration()
            {
                UrlReservations = new UrlReservations()
                {
                    CreateAutomatically = true
                }
            });*/
        }

        public void Start()
        {
            if (!TryStartListener())
            {
                if (!TryAddUrlReservations())
                {
                    Log.ErrorFormat("Unable to add url reservations for web server.");
                    return;
                }

                if (!TryStartListener())
                {
                    Log.ErrorFormat("Unable to start listener");
                    return;
                }
            }

            WsServer.Start();
            _proxy.Start();
            Log.InfoFormat("Web server started.");
        }

        public void Stop()
        {
            _host.Dispose();
            WsServer.Stop();
            _proxy.Dispose();
            Log.InfoFormat("Web server stopped.");
        }

        private bool TryStartListener()
        {
            try
            {
                _host = Program.CreateHostBuilder(new string[0])
                    .ConfigureWebHost(wh =>
                    {
                        wh.Configure(ConfigureWebApp)
                            .UseUrls($"http://+:{PrivateWebPort}");
                    })
                    .Build();

                _host.StartAsync();

                return true;
            }
            catch (TargetInvocationException e)
            {
                var innerException = e.InnerException as HttpListenerException;
                if (innerException?.ErrorCode == 5) // 5 == AccessDenied
                {
                    return false;
                }

                throw;
            }
        }

        private void ConfigureWebApp(IApplicationBuilder app)
        {
            var contentTypeProvider = new FileExtensionContentTypeProvider();

            if (!contentTypeProvider.Mappings.ContainsKey(".json"))
            {
                contentTypeProvider.Mappings.Add(".json", "application/json");
            }

            app.UseFileServer(new FileServerOptions()
            {
                RequestPath = new PathString("/tiles"),
                FileProvider = new PhysicalFileProvider(MiMapConfig.Config.TilesDirectory),
#if DEBUG
                EnableDirectoryBrowsing = true,
#endif
                EnableDefaultFiles = true,
                StaticFileOptions =
                {
                    ContentTypeProvider = contentTypeProvider,
                }
            });

#if FALSE
            var contentFileSystem = new PhysicalFileSystem("S:\\Development\\Projects\\MiNET-MiMap\\MiMap.Web\\Content");
#else
            var contentFileSystem =
                new EmbeddedFileProvider(typeof(MiMapWebServer).Assembly, GetType().Namespace + ".Content");
#endif


            app.UseFileServer(new FileServerOptions
            {
                FileProvider = contentFileSystem,
#if DEBUG
                EnableDirectoryBrowsing = true,
#endif
                EnableDefaultFiles = true
            });

            // Widget Stuff
            WidgetManager.ConfigureHttp(app);
        }

        private bool TryAddUrlReservations()
        {
            foreach (var url in Urls)
            {
                if (!NetSh.AddUrlAcl(url, WindowsIdentity.GetCurrent().Name))
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}