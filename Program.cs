
using LuoliHelper.Entities;
using LuoliHelper.StaticClasses;
using LuoliHelper.Utils;
using System.Reflection;

namespace Notification
{
    public class Program
    {

        public static Config Config;

        private static bool init()
        {
            bool result = false;
            string configFolder = "configs";

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

            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            var fileVersion = fileVersionInfo.FileVersion;

            SLogger.Info($"CurrentDirectory:[{Environment.CurrentDirectory}]");
            SLogger.Info($"Current File Version:[{fileVersion}]");


            if (!(args is null) && args.Length > 0 && args[0] =="AutoStart") {
                SLogger.WriteInConsole = false;
            }

            SLogger.Debug($"WriteInConsole:[{SLogger.WriteInConsole}]");


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

            var app = builder.Build();

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
