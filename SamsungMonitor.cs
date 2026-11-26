using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

sealed class SamsungMonitor
{
    static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AsusMonitorKeys", "samsung.dat");

    public string Host { get; private set; }
    string fingerprint;
    string token;

    static SamsungMonitor()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    SamsungMonitor(string host)
    {
        IPAddress ip;
        if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.ToString() != host)
            throw new ArgumentException("모니터의 IPv4 주소를 입력하세요.");
        Host = ip.ToString();
    }

    public static SamsungMonitor Pair(string host)
    {
        var monitor = new SamsungMonitor(host);
        var reply = monitor.Call("createAccessToken", null);
        monitor.token = ReadField(reply, "AccessToken");
        return monitor;
    }

    public static SamsungMonitor Load()
    {
        if (!File.Exists(SettingsFile)) return null;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(SettingsFile), null, DataProtectionScope.CurrentUser);
        var data = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes));
        var monitor = new SamsungMonitor(data["Host"]);
        monitor.fingerprint = data["Fingerprint"];
        monitor.token = data["Token"];
        if (String.IsNullOrWhiteSpace(monitor.token) || String.IsNullOrWhiteSpace(monitor.fingerprint))
            throw new InvalidDataException("저장된 페어링 정보를 읽을 수 없습니다.");
        return monitor;
    }

    public void Save()
    {
        var data = new Dictionary<string, string> {
            { "Host", Host }, { "Fingerprint", fingerprint }, { "Token", token }
        };
        var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data));
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
        string temp = SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, encrypted);
            if (File.Exists(SettingsFile)) File.Replace(temp, SettingsFile, null);
            else File.Move(temp, SettingsFile);
        }
        finally { File.Delete(temp); }
    }

    public string Adjust(bool volume, int direction)
    {
        return Describe(volume, AdjustPercent(volume, direction));
    }

    public string Describe(bool volume, int percent)
    {
        return String.Format("{0}   {1} {2}%  ({3}/{4})", Host, volume ? "볼륨" : "밝기",
            percent, volume ? percent : percent / 2, volume ? 100 : 50);
    }

    public int ReadPercent(bool volume)
    {
        int maximum = volume ? 100 : 50;
        int level = ReadLevel(volume ? "directVolumeControl" : "backlightControl",
            volume ? "volume" : "backlight", maximum);
        return level * 100 / maximum;
    }

    public int AdjustPercent(bool volume, int direction)
    {
        int current = ReadPercent(volume);
        int target = Math.Max(0, Math.Min(100, current + Math.Sign(direction) * 10));
        return current == target ? current : SetPercent(volume, target);
    }

    public int SetPercent(bool volume, int percent)
    {
        if (percent < 0 || percent > 100 || (!volume && percent % 2 != 0))
            throw new ArgumentOutOfRangeException("percent");
        string method = volume ? "directVolumeControl" : "backlightControl";
        string field = volume ? "volume" : "backlight";
        Call(method, new Dictionary<string, object> { { field, volume ? percent : percent / 2 } });
        int current = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt != 0) Thread.Sleep(200);
            current = ReadPercent(volume);
            if (current == percent) return current;
        }
        throw new InvalidOperationException(String.Format(
            "요청한 값은 {0}%이고 모니터에서 읽은 값은 {1}%입니다.", percent, current));
    }

    int ReadLevel(string method, string field, int maximum)
    {
        int value;
        if (!Int32.TryParse(ReadField(Call(method, null), field), out value) || value < 0 || value > maximum)
            throw new InvalidDataException("모니터에서 보고한 조절 범위를 확인할 수 없습니다.");
        return value;
    }

    static string ReadField(object result, string key)
    {
        var fields = result as Dictionary<string, object>;
        object value;
        if (fields == null || !fields.TryGetValue(key, out value) || value == null ||
            String.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
            throw new InvalidDataException("모니터 응답에 필요한 값이 없습니다 (" + key + ").");
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    object Call(string method, Dictionary<string, object> parameters)
    {
        bool pairing = method == "createAccessToken";
        var payload = new Dictionary<string, object> {
            { "jsonrpc", "2.0" }, { "id", "1" }, { "method", method }
        };
        if (!pairing)
        {
            parameters = parameters ?? new Dictionary<string, object>();
            parameters.Add("AccessToken", token);
            payload.Add("params", parameters);
        }
        var json = new JavaScriptSerializer();
        var body = Encoding.UTF8.GetBytes(json.Serialize(payload));
        var request = (HttpWebRequest)WebRequest.Create(new UriBuilder("https", Host, 1516).Uri);
        request.Method = "POST";
        request.Proxy = null;
        request.AllowAutoRedirect = false;
        request.KeepAlive = false;
        request.ServicePoint.Expect100Continue = false;
        request.Accept = request.ContentType = "application/json";
        request.ContentLength = body.Length;
        request.Timeout = request.ReadWriteTimeout = pairing ? 70000 : 5000;
        bool certificateChanged = false;
        request.ServerCertificateValidationCallback = (sender, cert, chain, errors) => {
            if (cert == null) return false;
            using (var sha = SHA256.Create())
            {
                string seen = BitConverter.ToString(sha.ComputeHash(cert.GetRawCertData())).Replace("-", "");
                if (pairing) fingerprint = seen;
                certificateChanged = !String.Equals(seen, fingerprint, StringComparison.Ordinal);
                return !certificateChanged;
            }
        };

        HttpWebResponse response;
        try
        {
            using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            response = (HttpWebResponse)request.GetResponse();
        }
        catch (WebException ex)
        {
            if (certificateChanged)
                throw new InvalidOperationException("인증서가 변경되었습니다. 모니터 IP를 확인한 뒤 다시 페어링하세요.", ex);
            response = ex.Response as HttpWebResponse;
            if (response == null)
                throw new IOException("모니터 IP, 전원 및 IP 리모컨 설정을 확인하세요. " + ex.Message, ex);
        }
        using (response)
        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            var buffer = new char[32769];
            int length = reader.ReadBlock(buffer, 0, buffer.Length);
            if (length == buffer.Length) throw new InvalidDataException("모니터 응답이 너무 큽니다.");
            var reply = json.Deserialize<Dictionary<string, object>>(new string(buffer, 0, length));
            if (reply == null) throw new InvalidDataException("모니터가 빈 응답을 보냈습니다.");
            object result;
            if (response.StatusCode == HttpStatusCode.OK && reply.TryGetValue("result", out result))
                return result;
            object error;
            var detail = reply.TryGetValue("error", out error) ? error as Dictionary<string, object> : reply;
            object rawCode;
            string code = detail != null && detail.TryGetValue("code", out rawCode)
                ? Convert.ToString(rawCode, CultureInfo.InvariantCulture) : "HTTP " + (int)response.StatusCode;
            if (code == "-32010" || code == "-32700")
                throw new InvalidOperationException("페어링 버튼을 누르고 모니터에서 연결을 허용하세요 (" + code + ").");
            throw new InvalidOperationException("모니터가 " + method + " 요청을 처리하지 못했습니다 (" + code + ").");
        }
    }
}
