#if FANTASY_NET
using System;
using System.Net;
using System.Text;
using Fantasy.Assembly;
using Fantasy.Network.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable CS8604 // Possible null reference argument.

// ReSharper disable PossibleMultipleEnumeration

namespace Fantasy.Network.HTTP
{
    /// <summary>
    /// HTTP服务配置事件 - 用于配置依赖注入服务
    /// 对应 ASP.NET Core 的 ConfigureServices 阶段
    /// </summary>
    public struct OnConfigureHttpServices
    {
        /// <summary>
        /// 当前所属的Scene
        /// </summary>
        public readonly Scene Scene;
        /// <summary>
        /// WebApplicationBuilder，用于配置服务容器
        /// </summary>
        public readonly WebApplicationBuilder Builder;
        /// <summary>
        /// IMvcBuilder，用于配置MVC选项、过滤器、JSON序列化等
        /// </summary>
        public readonly IMvcBuilder MvcBuilder;

        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="builder"></param>
        /// <param name="mvcBuilder"></param>
        public OnConfigureHttpServices(Scene scene, WebApplicationBuilder builder, IMvcBuilder mvcBuilder)
        {
            Scene = scene;
            Builder = builder;
            MvcBuilder = mvcBuilder;
        }
    }

    /// <summary>
    /// HTTP应用配置事件 - 用于配置中间件管道
    /// 对应 ASP.NET Core 的 Configure 阶段
    /// </summary>
    public struct OnConfigureHttpApplication
    {
        /// <summary>
        /// 当前所属的Scene
        /// </summary>
        public readonly Scene Scene;
        /// <summary>
        /// WebApplication，用于配置请求管道和中间件
        /// </summary>
        public readonly WebApplication Application;

        /// <summary>
        /// 构造方法
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="application"></param>
        public OnConfigureHttpApplication(Scene scene, WebApplication application)
        {
            Scene = scene;
            Application = application;
        }
    }
    
    /// <summary>
    /// HTTP服务器
    /// </summary>
    public sealed class HTTPServerNetwork : ANetwork
    {
        /// <summary>
        /// 初始化入口
        /// </summary>
        /// <param name="networkTarget"></param>
        /// <param name="bindIp"></param>
        /// <param name="port"></param>
        public void Initialize(NetworkTarget networkTarget, string bindIp, int port)
        {
            base.Initialize(NetworkType.Server, NetworkProtocolType.HTTP, networkTarget);

            try
            {
                StartAsync(bindIp, port);
            }
            catch (HttpListenerException e)
            {
                if (e.ErrorCode == 5)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("CMD管理员中输入下面命令:");
                    sb.AppendLine($"netsh http add urlacl url=http://{bindIp}:{port}/ user=Everyone");
                    throw new Exception(sb.ToString(), e);
                }

                Log.Error(e);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void StartAsync(string bindIp, int port)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            // 将Scene注册到 DI 容器中，传递给控制器
            builder.Services.AddSingleton(Scene);
            // 注册Scene同步过滤器
            builder.Services.AddScoped<SceneContextFilter>();
            // 注册控制器服务，使用框架默认配置
            var mvcBuilder = builder.Services.AddControllers()
                .AddJsonOptions(options => { options.JsonSerializerOptions.PropertyNamingPolicy = null; });
            // 触发服务配置事件，允许开发者注册自定义服务并配置MVC选项
            Scene.EventComponent.PublishAsync(new OnConfigureHttpServices(Scene, builder, mvcBuilder)).GetAwaiter().GetResult();
            // 添加所有程序集的控制器部件
            foreach (var assemblyManifest in AssemblyManifest.GetAssemblyManifest)
            {
                mvcBuilder.AddApplicationPart(assemblyManifest.Assembly);
            }
            var app = builder.Build();
            var listenUrl = $"http://{bindIp}:{port}/";
            app.Urls.Add(listenUrl);
            // 启用开发者异常页面（作为第一个中间件）
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            // 触发应用配置事件，允许开发者添加自定义中间件（CORS、认证、授权等）
            Scene.EventComponent.PublishAsync(new OnConfigureHttpApplication(Scene, app)).GetAwaiter().GetResult();
            // 端点映射（必须在最后）
            app.MapControllers();
            // 开启监听
            app.RunAsync();
            Log.Info($"SceneConfigId = {Scene.SceneConfigId} HTTPServer Listen {listenUrl}");
        }

        /// <summary>
        /// 移除Channel
        /// </summary>
        /// <param name="channelId"></param>
        /// <exception cref="NotImplementedException"></exception>
        public override void RemoveChannel(uint channelId)
        {
            throw new NotImplementedException();
        }
    }
}
#endif