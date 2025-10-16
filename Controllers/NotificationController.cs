using LuoliHelper.Entities;
using LuoliHelper.Enums;
using LuoliHelper.StaticClasses;
using LuoliHelper.Utils;
using MethodTimer;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RestSharp;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Notification.Controllers
{
    public class NotificationController : Controller
    {

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(Program.Config.SemaphoreSlim);

        public class NotifyRequest
        {
            public string msgType { get; set; }
            public string content { get; set; }
            public string toUser { get; set; }
        }


        [HttpPost]
        [Route("api/notification/notify")]
        [Time]
        public async Task<ApiResponse<string>> NotifyMsg([FromBody] NotifyRequest json)
        {
            ApiResponse<string> response = new ApiResponse<string>();

            SLogger.Info($"trigger NotificationController.NotifyMsg with {json}");


            bool acquired = await _semaphore.WaitAsync(300);
            if (!acquired)
            {
                response.msg = "busy now";
                response.code = EResponseCode.Fail;
                return response;
            }

            try
            {
                if (json.toUser == "")
                    json.toUser = "@all";

                var notifyResult = await weComNotify(json.content, json.toUser);
                if (!notifyResult.Item1)
                {
                    response.msg = $"Server酱 执行错误: {notifyResult.Item2}";
                    response.code = EResponseCode.Fail;
                }
                else
                {
                    response.msg = "send success";
                    response.code = EResponseCode.Success;
                }

            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                response.code = EResponseCode.Fail;
                SLogger.Error(ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }

            response.data = string.Empty;

            return response;
        }


        private string getWeComToken()
        {
            try
            {
                if (RedisHelper.Exists("WeComToken"))
                    return RedisHelper.Get("WeComToken");

                // 获取Token
                string getTokenUrl = string.Intern($"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Program.Config.KVPairs["WeComCorpId"]}&corpsecret={Program.Config.KVPairs["WeComSecret"]}");

                var result = JsonConvert
                       .DeserializeObject<dynamic>(new RestClient(getTokenUrl)
                       .Get(new RestRequest()).Content);

                string token = result.access_token;

                RedisHelper.SetAsync("WeComToken", token, 60 * 15); //15min

                SLogger.Info($"Got Token from weixin api:{token}");
                return token;
            }
            catch (Exception ex)
            {
                SLogger.Error($"GetWeComToken error:{ex.Message}");
                return "";
            }
        }

        private async Task<(bool, string)> weComNotify(string msg, string weComTouId)
        {
            string token = getWeComToken();

            try
            {
                if (String.IsNullOrWhiteSpace(token))
                {
                    SLogger.Error("WeComNotify get token fail");
                    return (false, "WeComNotify get token fail");
                }

                var data = new
                {
                    touser = weComTouId,
                    agentid = Program.Config.KVPairs["WeAgentId"],
                    msgtype = "text",
                    text = new
                    {
                        content = msg
                    },
                    duplicate_check_interval = 600
                };
                string serJson = JsonConvert.SerializeObject(data);

                var response = await ApiCaller.PostAsync($"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={token}", serJson);
                string resContent = await response.Content.ReadAsStringAsync();
                return (true, resContent);
            }
            catch (Exception ex)
            {
                SLogger.Error($"WeComNotify error:{ex.Message}");
                return (false, ex.Message);
            }

        }



    }
}
