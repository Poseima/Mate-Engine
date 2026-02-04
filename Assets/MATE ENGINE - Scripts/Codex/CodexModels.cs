using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MateEngine.Codex
{
    // ── JSON-RPC envelope ──────────────────────────────────────────

    [Serializable]
    public class JsonRpcRequest
    {
        public string id;
        public string method;
        public object @params;

        public JsonRpcRequest(string method, object @params = null)
        {
            this.id = Guid.NewGuid().ToString("N").Substring(0, 12);
            this.method = method;
            this.@params = @params;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, SerSettings);

        static readonly JsonSerializerSettings SerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    [Serializable]
    public class JsonRpcNotification
    {
        public string method;
        public object @params;

        public JsonRpcNotification(string method, object @params = null)
        {
            this.method = method;
            this.@params = @params;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });
    }

    [Serializable]
    public class JsonRpcError
    {
        public int code;
        public string message;
        public JToken data;
    }

    // ── Initialize ─────────────────────────────────────────────────

    [Serializable]
    public class InitializeParams
    {
        public ClientInfo clientInfo;
    }

    [Serializable]
    public class ClientInfo
    {
        public string name;
        public string title;
        public string version;
    }

    // ── Auth params ────────────────────────────────────────────────

    [Serializable]
    public class LoginApiKeyParams
    {
        public string type = "apiKey";
        public string apiKey;
    }

    [Serializable]
    public class LoginChatGptParams
    {
        public string type = "chatgpt";
    }

    [Serializable]
    public class LoginCompletedNotification
    {
        public string loginId;
        public bool success;
        public string error;
    }

    // ── Auth status ─────────────────────────────────────────────────

    [Serializable]
    public class GetAuthStatusParams
    {
        public bool includeToken = false;
        public bool refreshToken = true;
    }

    // ── Thread params ──────────────────────────────────────────────

    [Serializable]
    public class ThreadStartParams
    {
        public string model;
        public string modelProvider;
        public string baseInstructions;
        public string developerInstructions;
        public string approvalPolicy = "never";
        public string cwd;
        public bool ephemeral;
    }

    [Serializable]
    public class ThreadResumeParams
    {
        public string threadId;
    }

    // ── Turn params ────────────────────────────────────────────────

    [Serializable]
    public class TurnStartParams
    {
        public string threadId;
        public List<UserInput> input;
        public string cwd;
        public string approvalPolicy;
        public CollaborationModeParam collaborationMode;
        public SandboxPolicyParam sandboxPolicy;
        public string providerId;
    }

    [Serializable]
    public class CollaborationModeParam
    {
        public string mode = "custom";
        public CollaborationModeSettings settings;
    }

    [Serializable]
    public class CollaborationModeSettings
    {
        public string model = "";
        public string reasoning_effort = "medium";
        public string developer_instructions;
    }

    [Serializable]
    public class SandboxPolicyParam
    {
        public string type = "workspaceWrite";
        public string[] writableRoots = new string[0];
        public bool networkAccess = true;
        public bool excludeTmpdirEnvVar = false;
        public bool excludeSlashTmp = false;
    }

    [Serializable]
    public class UserInput
    {
        public string type = "text";
        public string text;
    }

    // ── Provider list ─────────────────────────────────────────────

    [Serializable]
    public class ProviderListParams {}

    [Serializable]
    public class ProviderInfo
    {
        public string id;
        public string name;
        public bool requiresOpenaiAuth;
        public string wireApi;
        public string baseUrl;
        public string envKey;
    }

    [Serializable]
    public class TurnInterruptParams
    {
        public string threadId;
        public string turnId;
    }

    // ── Model list ─────────────────────────────────────────────────

    [Serializable]
    public class ModelListParams
    {
        public string cursor;
        public int? limit;
    }

    [Serializable]
    public class ModelInfo
    {
        public string id;
        public string model;
        public string displayName;
        public string description;
        public bool isDefault;
    }

    // ── Server → Client request approval (auto-accept) ─────────────

    [Serializable]
    public class ApprovalResponse
    {
        public string decision = "accept";
    }

    // ── Parsed line helper ─────────────────────────────────────────

    public static class CodexLineParser
    {
        /// <summary>
        /// Check if line is a notification (has method, no id).
        /// </summary>
        public static bool IsNotification(string line, out string method, out JToken @params)
        {
            method = null;
            @params = null;
            try
            {
                var obj = JObject.Parse(line);
                if (obj["method"] != null && obj["id"] == null)
                {
                    method = obj["method"].Value<string>();
                    @params = obj["params"];
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if line is a response (has id + result or error).
        /// </summary>
        public static bool IsResponse(string line, out string id, out JToken result, out JsonRpcError error)
        {
            id = null;
            result = null;
            error = null;
            try
            {
                var obj = JObject.Parse(line);
                if (obj["id"] != null && (obj["result"] != null || obj["error"] != null))
                {
                    id = obj["id"].Value<string>();
                    result = obj["result"];
                    if (obj["error"] != null)
                        error = obj["error"].ToObject<JsonRpcError>();
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Check if line is a server-to-client request (has id + method).
        /// These are approval requests that need a response.
        /// </summary>
        public static bool IsServerRequest(string line, out string id, out string method, out JToken @params)
        {
            id = null;
            method = null;
            @params = null;
            try
            {
                var obj = JObject.Parse(line);
                if (obj["id"] != null && obj["method"] != null)
                {
                    id = obj["id"].Value<string>();
                    method = obj["method"].Value<string>();
                    @params = obj["params"];
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
