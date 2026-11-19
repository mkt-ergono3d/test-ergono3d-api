using Microsoft.AspNetCore.Mvc;

namespace InsoleApi.Common
{
    /// <summary>
    /// 通用的结果类
    /// </summary>
    public class Result
    {
        public int Code { get; set; } = -1;

        public string? Message { get; set; }

        public object? Data { get; set; }

        public static Result Success(object? data, string? message = null, int code = 0)
        {
            return new Result { Code = code, Message = message, Data = data };
        }

        public static Result Failed(int code, string? message = null, object? data = null)
        {
            return new Result { Code = code, Message = message, Data = data };
        }
    }
}
