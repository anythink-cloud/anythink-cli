namespace AnythinkCli.Tests;

internal static class N8nFixtures
{
    public const string Base = "https://n8n.example.com";
    public const string KeyUnderTest = "n8n_api_7c1d9e5f3b2a4680";
    public const string CredentialId = "cred-9f3a77c2";
    public const string ListPath = "/api/v1/workflows?limit=100";

    public const string OrderAlerts = """
    {
      "id": "101", "name": "Order alerts", "active": true,
      "nodes": [
        { "id": "n1", "name": "Webhook", "type": "n8n-nodes-base.webhook", "typeVersion": 2, "parameters": { "path": "orders/new", "httpMethod": "POST" } },
        { "id": "n2", "name": "Fetch order", "type": "n8n-nodes-base.httpRequest", "typeVersion": 4.2,
          "parameters": {
            "method": "get", "url": "https://billing.example.com/orders?api_key=abc123&page=1",
            "authentication": "genericCredentialType", "genericAuthType": "httpHeaderAuth",
            "sendHeaders": true, "headerParameters": { "parameters": [ { "name": "Authorization", "value": "Bearer live-token-xyz" }, { "name": "Accept", "value": "application/json" } ] }
          },
          "credentials": { "httpHeaderAuth": { "id": "cred-9f3a77c2", "name": "Billing API key" } } },
        { "id": "n3", "name": "Shape payload", "type": "n8n-nodes-base.set", "typeVersion": 3.4,
          "parameters": { "assignments": { "assignments": [
            { "id": "a1", "name": "source", "value": "n8n", "type": "string" },
            { "id": "a2", "name": "retries", "value": "3", "type": "number" },
            { "id": "a3", "name": "total", "value": "={{ $json.total }}", "type": "string" } ] } } },
        { "id": "n4", "name": "Is big order", "type": "n8n-nodes-base.if", "typeVersion": 2, "parameters": { "conditions": { "conditions": [] } } },
        { "id": "n5", "name": "Notify Slack", "type": "n8n-nodes-base.slack", "typeVersion": 2.2, "parameters": { "channel": "#orders" },
          "credentials": { "slackApi": { "id": "cred-slack-1", "name": "Ops Slack" } } },
        { "id": "n6", "name": "Do nothing", "type": "n8n-nodes-base.noOp", "typeVersion": 1, "parameters": {} },
        { "id": "n7", "name": "Sticky Note", "type": "n8n-nodes-base.stickyNote", "typeVersion": 1, "parameters": { "content": "remember to rotate keys" } }
      ],
      "connections": {
        "Webhook": { "main": [ [ { "node": "Fetch order", "type": "main", "index": 0 } ] ] },
        "Fetch order": { "main": [ [ { "node": "Shape payload", "type": "main", "index": 0 } ] ] },
        "Shape payload": { "main": [ [ { "node": "Is big order", "type": "main", "index": 0 } ] ] },
        "Is big order": { "main": [ [ { "node": "Notify Slack", "type": "main", "index": 0 } ], [ { "node": "Do nothing", "type": "main", "index": 0 } ] ] }
      }
    }
    """;

    public const string NightlySync = """
    {
      "id": "102", "name": "Nightly sync", "active": false,
      "nodes": [
        { "id": "m1", "name": "Every two hours", "type": "n8n-nodes-base.scheduleTrigger", "typeVersion": 1.2,
          "parameters": { "rule": { "interval": [ { "field": "hours", "hoursInterval": 2, "triggerAtMinute": 15 } ] } } },
        { "id": "m2", "name": "Run code", "type": "n8n-nodes-base.code", "typeVersion": 2, "parameters": { "jsCode": "return [{ json: { ok: true } }];" } }
      ],
      "connections": { "Every two hours": { "main": [ [ { "node": "Run code", "type": "main", "index": 0 } ] ] } }
    }
    """;

    public static string Page(string workflows, string? nextCursor = null) =>
        $$"""{ "data": [ {{workflows}} ], "nextCursor": {{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}} }""";

    public static string Node(string name, string type, string parameters = "{}", string extra = "") =>
        $$"""{ "name": "{{name}}", "type": "{{type}}", "typeVersion": 1, "parameters": {{parameters}} {{extra}} }""";

    public static string Workflow(string id, string name, string nodes, string connections = "{}") =>
        $$"""{ "id": "{{id}}", "name": "{{name}}", "nodes": [ {{nodes}} ], "connections": {{connections}} }""";
}
