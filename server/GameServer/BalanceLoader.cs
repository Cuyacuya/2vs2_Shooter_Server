using System.IO;
using System.Text.Json;
using Shared;

namespace GameServer
{
    // 서버 측 balance.json 로딩 helper.
    // System.Text.Json은 .NET 8에 기본 포함되지만 netstandard2.1(Shared)에는 없어서
    // Shared 프로젝트는 POCO만 들고 있고, JSON 파싱은 서버 측에서 담당.
    public static class BalanceLoader
    {
        public static void LoadFromFile(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var loaded = JsonSerializer.Deserialize<BalanceConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (loaded != null) Balance.Current = loaded;
        }
    }
}
