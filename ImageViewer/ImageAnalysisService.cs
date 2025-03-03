using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography;

namespace ImageViewer
{
    public class ImageAnalysisService
    {
        private readonly string apiKey;
        private readonly string secretKey;
        private string accessToken;
        private DateTime tokenExpireTime;
        private readonly string cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageViewer",
            "AnalysisCache"
        );
        private static readonly HttpClient httpClient = new HttpClient();

        public ImageAnalysisService(string apiKey, string secretKey)
        {
            this.apiKey = apiKey;
            this.secretKey = secretKey;
            Directory.CreateDirectory(cacheFolder);
        }

        private string GetCacheFilePath(string imagePath)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(imagePath));
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return Path.Combine(cacheFolder, $"{hashString}.json");
            }
        }

        private async Task EnsureAccessToken()
        {
            try
            {
                if (string.IsNullOrEmpty(accessToken) || DateTime.Now >= tokenExpireTime)
                {
                    var tokenUrl = $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={apiKey}&client_secret={secretKey}";
                    var response = await httpClient.GetStringAsync(tokenUrl);

                    // 显示 token 响应
                    MessageBox.Show($"Token 响应：\n{response}", "Token 信息");

                    var json = JObject.Parse(response);
                    if (json["error"] != null)
                    {
                        throw new Exception($"获取 token 失败: {json["error"]} - {json["error_description"]}");
                    }

                    accessToken = json["access_token"].ToString();
                    tokenExpireTime = DateTime.Now.AddDays(29);

                    MessageBox.Show($"获取到的 Token：{accessToken}", "Token");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取 Token 失败：{ex.Message}", "错误");
                throw;
            }
        }

        public async Task<JObject> AnalyzeImageAsync(string imagePath)
        {
            var cacheFile = GetCacheFilePath(imagePath);
            if (File.Exists(cacheFile))
            {
                try
                {
                    var cached = JObject.Parse(File.ReadAllText(cacheFile));
                    return cached;
                }
                catch
                {
                    // 如果缓存文件损坏，忽略错误继续处理
                }
            }

            try
            {
                await EnsureAccessToken();
                byte[] imageBytes = File.ReadAllBytes(imagePath);
                string base64Image = Convert.ToBase64String(imageBytes);
                var results = new JObject();

                // 只使用最可靠的 API
                var advancedResult = await SendRequest(
                    "https://aip.baidubce.com/rest/2.0/image-classify/v2/advanced_general",
                    base64Image
                );

                results["advanced"] = advancedResult;

                // 保存缓存
                File.WriteAllText(cacheFile, results.ToString(Newtonsoft.Json.Formatting.None));
                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"分析图片失败: {ex.Message}");
                return null;
            }
        }

        private async Task<JObject> SendRequest(string url, string base64Image)
        {
            try
            {
                // 检查图片大小
                if (base64Image.Length > 4 * 1024 * 1024)  // 4MB
                {
                    throw new Exception("图片太大，请选择小于4MB的图片");
                }

                // 使用表单数据格式
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("image", base64Image),
                    new KeyValuePair<string, string>("baike_num", "5")
                };

                var content = new FormUrlEncodedContent(formData);

                // 添加必要的请求头
                httpClient.DefaultRequestHeaders.Clear();

                var response = await httpClient.PostAsync($"{url}?access_token={accessToken}", content);
                var responseText = await response.Content.ReadAsStringAsync();

                // 调试用：显示 API 返回的数据
                System.Diagnostics.Debug.WriteLine($"API 返回数据: {responseText}");

                var result = JObject.Parse(responseText);
                if (result["error_code"] != null)
                {
                    throw new Exception($"API 错误：{result["error_code"]} - {result["error_msg"]}");
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"请求失败：{ex.Message}");
                return new JObject();
            }
        }

        public bool MatchesDescription(JObject analysis, string searchText)
        {
            if (analysis == null || string.IsNullOrWhiteSpace(searchText))
                return false;

            var keywords = searchText.ToLower().Split(new[] { ' ', '，', '、' },
                StringSplitOptions.RemoveEmptyEntries);

            try
            {
                // 检查 advanced 中的结果
                if (analysis["advanced"] is JObject advanced)
                {
                    var results = advanced["result"] as JArray;
                    if (results != null)
                    {
                        foreach (var result in results)
                        {
                            double score = result["score"]?.Value<double>() ?? 0;
                            if (score < 0.05) continue; // 降低置信度阈值

                            foreach (var searchKeyword in keywords)
                            {
                                // 1. 检查关键词
                                var keyword = result["keyword"]?.ToString().ToLower();
                                if (!string.IsNullOrEmpty(keyword))
                                {
                                    // 完全匹配或部分匹配
                                    if (keyword.Contains(searchKeyword) || searchKeyword.Contains(keyword))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"匹配成功: {keyword} -> {searchKeyword}");
                                        return true;
                                    }
                                }

                                // 2. 检查分类
                                var root = result["root"]?.ToString().ToLower();
                                if (!string.IsNullOrEmpty(root))
                                {
                                    if (root.Contains(searchKeyword) || searchKeyword.Contains(root))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"分类匹配成功: {root} -> {searchKeyword}");
                                        return true;
                                    }
                                }

                                // 3. 检查百科描述
                                var baikeInfo = result["baike_info"];
                                if (baikeInfo != null)
                                {
                                    var description = baikeInfo["description"]?.ToString().ToLower();
                                    if (!string.IsNullOrEmpty(description) && description.Contains(searchKeyword))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"百科匹配成功: {description} -> {searchKeyword}");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 直接检查 result 数组
                    var results = analysis["result"] as JArray;
                    if (results != null)
                    {
                        foreach (var result in results)
                        {
                            double score = result["score"]?.Value<double>() ?? 0;
                            if (score < 0.05) continue; // 降低置信度阈值

                            foreach (var searchKeyword in keywords)
                            {
                                // 1. 检查关键词
                                var keyword = result["keyword"]?.ToString().ToLower();
                                if (!string.IsNullOrEmpty(keyword))
                                {
                                    // 完全匹配或部分匹配
                                    if (keyword.Contains(searchKeyword) || searchKeyword.Contains(keyword))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"匹配成功: {keyword} -> {searchKeyword}");
                                        return true;
                                    }
                                }

                                // 2. 检查分类
                                var root = result["root"]?.ToString().ToLower();
                                if (!string.IsNullOrEmpty(root))
                                {
                                    if (root.Contains(searchKeyword) || searchKeyword.Contains(root))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"分类匹配成功: {root} -> {searchKeyword}");
                                        return true;
                                    }
                                }

                                // 3. 检查百科描述
                                var baikeInfo = result["baike_info"];
                                if (baikeInfo != null)
                                {
                                    var description = baikeInfo["description"]?.ToString().ToLower();
                                    if (!string.IsNullOrEmpty(description) && description.Contains(searchKeyword))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"百科匹配成功: {description} -> {searchKeyword}");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"匹配描述失败: {ex.Message}");
            }

            return false;
        }
    }
}