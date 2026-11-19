using System.Text;

namespace InsoleApi.Utils
{
    public static class StlResponseInspector
    {
        public static string? GetEmptyStlReason(string stlBase64, out int decodedBytes)
        {
            decodedBytes = 0;
            if (string.IsNullOrWhiteSpace(stlBase64))
                return "STL generation returned an empty response body.";

            try
            {
                var base64 = stlBase64;
                var index = base64.IndexOf(",");
                if (index >= 0)
                    base64 = base64[(index + 1)..];

                var bytes = Convert.FromBase64String(base64);
                decodedBytes = bytes.Length;
                if (bytes.Length == 0)
                    return "STL generation returned a base64 payload with 0 decoded bytes.";

                if (IsBinaryStl(bytes))
                {
                    var triangleCount = BitConverter.ToUInt32(bytes, 80);
                    return triangleCount == 0 ? "STL generation returned a binary STL with 0 triangles." : null;
                }

                var asciiStl = Encoding.ASCII.GetString(bytes);
                return asciiStl.Contains("facet", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : "STL generation returned an ASCII STL without any facets.";
            }
            catch (FormatException)
            {
                return "STL generation response is not valid base64.";
            }
        }

        private static bool IsBinaryStl(byte[] bytes)
        {
            if (bytes.Length < 84)
                return false;

            var triangleCount = BitConverter.ToUInt32(bytes, 80);
            var expectedLength = 84L + triangleCount * 50L;
            return expectedLength == bytes.Length;
        }
    }
}
