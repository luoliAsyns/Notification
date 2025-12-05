
using LuoliCommon;
using LuoliCommon.Logger;
using LuoliHelper.Entities;
using LuoliHelper.StaticClasses;
using LuoliHelper.Utils;
using LuoliUtils;
using System.Reflection;

namespace Notification
{
    public class Program
    {

        public static Config Config;

        private static bool init()
        {
            bool result = false;
            string configFolder = "/app/Notification/configs";

#if DEBUG
            configFolder = "debugConfigs";
#endif

            ActionsOperator.TryCatchAction(() =>
            {
                Config = new Config($"{configFolder}/sys.json");

                new RedisConnection($"{configFolder}/redis.json");

                result = true;
            });

            return result;
        }


        public static void Main(string[] args)
        {

            #region luoli code

            Environment.CurrentDirectory = AppContext.BaseDirectory;

            if (!init())
            {
                throw new Exception("initial failed; cannot start");
            }

            #endregion



            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // 如果需要全局启用签名验证，可以在这里注册
            builder.Services.AddControllers(options =>
            {
                options.Filters.Add(new SignValidationAttribute(300, "luoliNotificationSecret"));
            });


            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            #region 注册ILogger

            builder.Services.AddHttpClient("LokiHttpClient")
                .ConfigureHttpClient(client =>
                {
                    // 可在这里统一配置 HttpClient（如代理、SSL 忽略，生产环境慎用）
                    // client.DefaultRequestHeaders.Add("X-Custom-Header", "luoli-app");
                });

            //这里是对原始http client logger进行filter
            builder.Logging.AddFilter(
                "System.Net.Http.HttpClient.LokiHttpClient",
                LogLevel.Warning
            );

            // 注册LokiLogger单例服务（封装日志操作）
            builder.Services.AddSingleton<LuoliCommon.Logger.ILogger, LokiLogger>(provider =>
            {
                var httpClient = provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LokiHttpClient");

                var loki = new LokiLogger(Config.KVPairs["LokiEndPoint"],
                    new Dictionary<string, string>(),
                    httpClient);
                loki.AfterLog = (msg) => Console.WriteLine(msg);

                ActionsOperator.Initialize(loki);
                return loki;
            });

            #endregion


            var app = builder.Build();

            ServiceLocator.Initialize(app.Services);

            #region luoli code

            // 应用启动后，通过服务容器获取 LokiLogger 实例
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    // 获取 LokiLogger 实例
                    var lokiLogger = services.GetRequiredService<LuoliCommon.Logger.ILogger>();

                    // 记录启动日志
                    lokiLogger.Info("应用程序启动成功");
                    lokiLogger.Debug($"环境:{app.Environment.EnvironmentName},端口：{Config.BindAddr}");


                    Assembly assembly = Assembly.GetExecutingAssembly();
                    var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    var fileVersion = fileVersionInfo.FileVersion;

                    lokiLogger.Info($"CurrentDirectory:[{Environment.CurrentDirectory}]");
                    lokiLogger.Info($"Current File Version:[{fileVersion}]");

                    ApiCaller.NotifyAsync($"{Config.ServiceName}.{Config.ServiceId} v{fileVersion} 启动了");

                }
                catch (Exception ex)
                {
                    // 启动日志失败时降级输出
                    Console.WriteLine($"启动日志记录失败：{ex.Message}");
                }
            }


            #endregion


            // Configure the HTTP request pipeline.
            //if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run(Config.BindAddr);
        }
    }
}
