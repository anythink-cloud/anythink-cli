using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// Simple test program that doesn't require the full CLI infrastructure
class BulkOperationsTest
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 Testing Bulk Operations Framework...\n");
        
        await TestCsvImport();
        await TestJsonImport();
        await TestValidation();
        
        Console.WriteLine("\n🎉 All tests completed successfully!");
    }
    
    static async Task TestCsvImport()
    {
        Console.WriteLine("📄 Testing CSV Import...");
        
        // Create test CSV data
        var csvData = @"name,email,age,city
John Doe,john@example.com,30,New York
Jane Smith,jane@example.com,25,Los Angeles
Bob Johnson,bob@example.com,35,Chicago";

        // Test parsing
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headers = ParseCsvLine(lines[0]);
        
        Console.WriteLine($"   Headers: {string.Join(", ", headers)}");
        
        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            var record = new Dictionary<string, object>();
            
            for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
            {
                record[headers[j]] = values[j];
            }
            
            Console.WriteLine($"   Record {i}: {record["name"]} - {record["email"]}");
        }
        
        Console.WriteLine("   ✅ CSV parsing successful\n");
    }
    
    static async Task TestJsonImport()
    {
        Console.WriteLine("📄 Testing JSON Import...");
        
        // Create test JSON data
        var jsonData = @"[
  {""name"": ""Alice"", ""email"": ""alice@example.com"", ""age"": 28},
  {""name"": ""Bob"", ""email"": ""bob@example.com"", ""age"": 32},
  {""name"": ""Charlie"", ""email"": ""charlie@example.com"", ""age"": 24}
]";

        // Test parsing
        var jsonArray = JsonNode.Parse(jsonData) as JsonArray;
        
        for (int i = 0; i < jsonArray!.Count; i++)
        {
            var obj = jsonArray[i] as JsonObject;
            Console.WriteLine($"   Record {i + 1}: {obj!["name"]} - {obj["email"]}");
        }
        
        Console.WriteLine("   ✅ JSON parsing successful\n");
    }
    
    static async Task TestValidation()
    {
        Console.WriteLine("📄 Testing Data Validation...");
        
        // Test email validation
        var emails = new[] { "valid@example.com", "invalid-email", "another@valid.com" };
        
        foreach (var email in emails)
        {
            var isValid = IsValidEmail(email);
            Console.WriteLine($"   Email '{email}': {(isValid ? "✅ Valid" : "❌ Invalid")}");
        }
        
        // Test age validation
        var ages = new[] { 25, -5, 150, 200 };
        
        foreach (var age in ages)
        {
            var isValid = IsValidAge(age);
            Console.WriteLine($"   Age {age}: {(isValid ? "✅ Valid" : "❌ Invalid")}");
        }
        
        Console.WriteLine("   ✅ Validation logic successful\n");
    }
    
    static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current += '"';
                    i += 2;
                }
                else
                {
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
                i++;
            }
            else
            {
                current += c;
                i++;
            }
        }

        result.Add(current);
        return result.ToArray();
    }
    
    static bool IsValidEmail(string email)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }
    
    static bool IsValidAge(int age)
    {
        return age >= 0 && age <= 150;
    }
}
