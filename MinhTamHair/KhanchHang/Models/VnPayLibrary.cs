using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MinhTamHair.KhanchHang.Models
{
    public class VnPayLibrary
    {
        private SortedList<string, string> _requestData = new SortedList<string, string>(new VnPayComparer());

        public void AddRequestData(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _requestData.Add(key, value);
            }
        }

        public string CreateRequestUrl(string baseUrl, string vnp_HashSecret)
        {
            StringBuilder data = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in _requestData)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    // Sử dụng WebUtility.UrlEncode để giống với cơ chế của VNPay hơn
                    data.Append(WebUtility.UrlEncode(kv.Key) + "=" + WebUtility.UrlEncode(kv.Value) + "&");
                }
            }

            string queryString = data.ToString();
            if (queryString.EndsWith("&"))
            {
                queryString = queryString.Remove(queryString.Length - 1);
            }

            // Logic quan trọng: VNPay tính toán chữ ký dựa trên chuỗi có dấu '+' thay cho dấu cách
            // Nếu dùng Uri.EscapeDataString nó sẽ ra %20 -> Gây sai chữ ký
            string vnp_SecureHash = HmacSHA512(vnp_HashSecret, queryString);

            return baseUrl + "?" + queryString + "&vnp_SecureHash=" + vnp_SecureHash;
        } 

        public string HmacSHA512(string key, string inputData)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] inputBytes = Encoding.UTF8.GetBytes(inputData);
            using (var hmac = new HMACSHA512(keyBytes))
            {
                byte[] hashValue = hmac.ComputeHash(inputBytes);
                // Trả về chuỗi Hex VIẾT HOA và không có dấu gạch ngang
                return BitConverter.ToString(hashValue).Replace("-", "").ToUpper();
            }
        }
    }

    public class VnPayComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            var vnpCompare = CompareInfo.GetCompareInfo("en-US");
            return vnpCompare.Compare(x, y, CompareOptions.Ordinal);
        }
    }
}