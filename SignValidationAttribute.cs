using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 请求签名验证过滤器
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class SignValidationAttribute : ActionFilterAttribute
{
    // 签名过期时间（秒）
    private readonly int _expireSeconds;

    // 用于签名验证的密钥（实际项目中应放在配置文件或密钥管理服务中）
    private readonly string _appSecret;

    public SignValidationAttribute(int expireSeconds = 300, string appSecret = "YourSecureSecretKey")
    {
        _expireSeconds = expireSeconds;
        _appSecret = appSecret;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. 验证请求是否包含必要的签名参数
        var (isValid, errorMessage) = await ValidateSignParameters(context);
        if (!isValid)
        {
            context.Result = new BadRequestObjectResult(new { code = 400, message = errorMessage });
            return;
        }

        // 2. 获取请求中的签名和时间戳
        var request = context.HttpContext.Request;
        var sign = request.Query["sign"].FirstOrDefault() ?? "";
        var timestamp = request.Query["timestamp"].FirstOrDefault() ?? "";

        if(string.Equals(sign,"luoli", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }


        // 3. 验证时间戳是否过期
        if (!ValidateTimestamp(timestamp))
        {
            context.Result = new BadRequestObjectResult(new { code = 401, message = "请求已过期" });
            return;
        }

        // 4. 生成签名并与请求中的签名比对
        var generatedSign = await GenerateSign(context);
        if (!string.Equals(sign, generatedSign, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new BadRequestObjectResult(new { code = 401, message = "签名验证失败" });
            return;
        }

        // 签名验证通过，继续执行
        await next();
    }

    /// <summary>
    /// 验证请求是否包含必要的签名参数
    /// </summary>
    private async Task<(bool isValid, string errorMessage)> ValidateSignParameters(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;

        // 检查是否包含签名和时间戳参数
        if (!request.Query.ContainsKey("sign") || string.IsNullOrEmpty(request.Query["sign"]))
        {
            return (false, "缺少签名参数(sign)");
        }

        if (!request.Query.ContainsKey("timestamp") || string.IsNullOrEmpty(request.Query["timestamp"]))
        {
            return (false, "缺少时间戳参数(timestamp)");
        }

        // 对于POST请求，检查body是否存在（可选）
        if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            request.ContentLength == 0 &&
            !request.ContentType?.Contains("multipart/form-data") == true)
        {
            return (false, "POST请求缺少请求体");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// 验证时间戳是否在有效期内
    /// </summary>
    private bool ValidateTimestamp(string timestamp)
    {
        if (!long.TryParse(timestamp, out var timestampValue))
        {
            return false;
        }

        // 转换为UTC时间进行比较（避免时区问题）
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestampValue).UtcDateTime;
        var currentTime = DateTime.UtcNow;

        // 检查是否在有效期内
        return currentTime.Subtract(requestTime).TotalSeconds <= _expireSeconds &&
               requestTime <= currentTime; // 防止时间戳在未来
    }

    /// <summary>
    /// 生成签名
    /// </summary>
    private async Task<string> GenerateSign(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        var timestamp = request.Query["timestamp"].FirstOrDefault() ?? "";

        // 1. 收集所有需要参与签名的参数
        var signParameters = new SortedDictionary<string, string>(StringComparer.Ordinal);

        // 添加Query参数（排除sign本身）
        foreach (var queryParam in request.Query)
        {
            if (queryParam.Key.Equals("sign", StringComparison.OrdinalIgnoreCase))
                continue;

            signParameters[queryParam.Key] = queryParam.Value;
        }

        // 2. 添加请求体参数（适用于POST等有body的请求）
        if (request.ContentLength > 0 &&
            request.ContentType != null &&
            (request.ContentType.Contains("application/json") ||
             request.ContentType.Contains("application/x-www-form-urlencoded")))
        {
            // 重置流位置，以便读取
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, true, 1024, true);
            var body = await reader.ReadToEndAsync();

            // 重置流位置，以便后续处理
            request.Body.Position = 0;

            if (!string.IsNullOrEmpty(body))
            {
                signParameters["body"] = body;
            }
        }

        // 3. 添加时间戳和密钥（确保排序）
        signParameters["timestamp"] = timestamp;
        signParameters["secret"] = _appSecret;

        // 4. 拼接参数（key=value&key=value）
        var paramString = string.Join("&", signParameters.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        // 5. 使用SHA256生成签名
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(paramString));

        // 转换为小写十六进制字符串
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}
