using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Validators;

/// <summary>
/// Basic data validator with configurable rules
/// </summary>
public class BasicDataValidator : IDataValidator
{
    private readonly ValidationSchema _schema;
    private readonly bool _strictMode;

    public BasicDataValidator(ValidationSchema schema, bool strictMode = false)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _strictMode = strictMode;
    }

    public async Task<ValidationResult> ValidateAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult { IsValid = true };

        foreach (var fieldDef in _schema.Fields.Values)
        {
            var fieldValue = GetFieldValue(record.Data, fieldDef.Name);
            var fieldResult = await ValidateFieldAsync(fieldDef, fieldValue, record.LineNumber);
            
            result.Errors.AddRange(fieldResult.Errors);
            result.Warnings.AddRange(fieldResult.Warnings);
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    public async Task<BatchValidationResult> ValidateBatchAsync(IEnumerable<DataRecord> records, CancellationToken cancellationToken = default)
    {
        var recordsList = records.ToList();
        var batchResult = new BatchValidationResult 
        { 
            IsValid = true, 
            TotalRecords = recordsList.Count 
        };

        var tasks = recordsList.Select(async (record, index) =>
        {
            var result = await ValidateAsync(record, cancellationToken);
            return new { Index = index, Result = result };
        });

        var results = await Task.WhenAll(tasks);

        foreach (var item in results)
        {
            if (item.Result.IsValid)
            {
                batchResult.ValidRecordIndices.Add(item.Index);
                batchResult.ValidRecords++;
            }
            else
            {
                batchResult.InvalidRecordIndices.Add(item.Index);
                batchResult.InvalidRecords++;
                batchResult.IsValid = false;
            }

            batchResult.Errors.AddRange(item.Result.Errors);
            batchResult.Warnings.AddRange(item.Result.Warnings);
        }

        return batchResult;
    }

    public ValidationSchema GetSchema() => _schema;

    private async Task<ValidationResult> ValidateFieldAsync(FieldDefinition fieldDef, object? value, int lineNumber)
    {
        var result = new ValidationResult { IsValid = true };

        // Required field validation
        if (fieldDef.Required && (value == null || (value is string str && string.IsNullOrWhiteSpace(str))))
        {
            result.Errors.Add(new ValidationError
            {
                LineNumber = lineNumber,
                Field = fieldDef.Name,
                Message = "Required field is missing or empty",
                AttemptedValue = value,
                ValidationRule = "required"
            });
            result.IsValid = false;
        }

        if (value == null) return result; // Skip other validations for null values

        // Type validation
        if (!string.IsNullOrEmpty(fieldDef.Type))
        {
            var typeResult = ValidateFieldType(fieldDef, value, lineNumber);
            if (!typeResult.IsValid)
            {
                result.Errors.AddRange(typeResult.Errors);
                result.IsValid = false;
            }
        }

        // Length validation
        if (value is string stringValue)
        {
            if (fieldDef.MinLength.HasValue && stringValue.Length < fieldDef.MinLength.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"String length {stringValue.Length} is less than minimum {fieldDef.MinLength}",
                    AttemptedValue = value,
                    ValidationRule = "minLength"
                });
                result.IsValid = false;
            }

            if (fieldDef.MaxLength.HasValue && stringValue.Length > fieldDef.MaxLength.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"String length {stringValue.Length} exceeds maximum {fieldDef.MaxLength}",
                    AttemptedValue = value,
                    ValidationRule = "maxLength"
                });
                result.IsValid = false;
            }
        }

        // Numeric range validation
        if (IsNumeric(value))
        {
            var numericValue = Convert.ToDouble(value);
            
            if (fieldDef.MinValue.HasValue && numericValue < fieldDef.MinValue.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"Value {numericValue} is less than minimum {fieldDef.MinValue}",
                    AttemptedValue = value,
                    ValidationRule = "minValue"
                });
                result.IsValid = false;
            }

            if (fieldDef.MaxValue.HasValue && numericValue > fieldDef.MaxValue.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"Value {numericValue} exceeds maximum {fieldDef.MaxValue}",
                    AttemptedValue = value,
                    ValidationRule = "maxValue"
                });
                result.IsValid = false;
            }
        }

        // Pattern validation
        if (!string.IsNullOrEmpty(fieldDef.Pattern) && value is string patternValue)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(fieldDef.Pattern);
                if (!regex.IsMatch(patternValue))
                {
                    result.Errors.Add(new ValidationError
                    {
                        LineNumber = lineNumber,
                        Field = fieldDef.Name,
                        Message = $"Value does not match required pattern",
                        AttemptedValue = value,
                        ValidationRule = "pattern"
                    });
                    result.IsValid = false;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"Invalid regex pattern: {ex.Message}",
                    AttemptedValue = fieldDef.Pattern,
                    ValidationRule = "pattern"
                });
            }
        }

        // Allowed values validation
        if (fieldDef.AllowedValues.Count > 0)
        {
            var valueString = value?.ToString();
            if (!fieldDef.AllowedValues.Contains(valueString))
            {
                result.Errors.Add(new ValidationError
                {
                    LineNumber = lineNumber,
                    Field = fieldDef.Name,
                    Message = $"Value '{valueString}' is not in allowed values: [{string.Join(", ", fieldDef.AllowedValues)}]",
                    AttemptedValue = value,
                    ValidationRule = "allowedValues"
                });
                result.IsValid = false;
            }
        }

        return await Task.FromResult(result);
    }

    private ValidationResult ValidateFieldType(FieldDefinition fieldDef, object value, int lineNumber)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            switch (fieldDef.Type.ToLowerInvariant())
            {
                case "string":
                case "varchar":
                case "text":
                    if (!(value is string))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected string, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;

                case "int":
                case "integer":
                    if (!IsInteger(value))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected integer, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;

                case "float":
                case "double":
                case "decimal":
                    if (!IsNumeric(value))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected number, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;

                case "bool":
                case "boolean":
                    if (!(value is bool))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected boolean, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;

                case "datetime":
                case "date":
                    if (!(value is DateTime) && !IsValidDateTimeString(value))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected datetime, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;

                case "json":
                    if (!(value is JsonObject || value is JsonArray || IsValidJsonString(value)))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            LineNumber = lineNumber,
                            Field = fieldDef.Name,
                            Message = $"Expected JSON, got {value.GetType().Name}",
                            AttemptedValue = value,
                            ValidationRule = "type"
                        });
                        result.IsValid = false;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ValidationError
            {
                LineNumber = lineNumber,
                Field = fieldDef.Name,
                Message = $"Type validation failed: {ex.Message}",
                AttemptedValue = value,
                ValidationRule = "type"
            });
            result.IsValid = false;
        }

        return result;
    }

    private static object? GetFieldValue(JsonObject data, string fieldName)
    {
        return data.ContainsKey(fieldName) ? data[fieldName]?.AsValue() : null;
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte || value is byte || value is short || value is ushort ||
               value is int || value is uint || value is long || value is ulong ||
               value is float || value is double || value is decimal ||
               (value is string str && (double.TryParse(str, out _) || decimal.TryParse(str, out _)));
    }

    private static bool IsInteger(object value)
    {
        return value is sbyte || value is byte || value is short || value is ushort ||
               value is int || value is uint || value is long || value is ulong ||
               (value is string str && long.TryParse(str, out _));
    }

    private static bool IsValidDateTimeString(object value)
    {
        if (value is string str)
            return DateTime.TryParse(str, out _) || DateTimeOffset.TryParse(str, out _);
        return false;
    }

    private static bool IsValidJsonString(object value)
    {
        if (value is string str)
        {
            try
            {
                JsonNode.Parse(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}
