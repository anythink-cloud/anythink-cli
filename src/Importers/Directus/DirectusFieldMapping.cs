namespace AnythinkCli.Importers.Directus;

// Maps Directus field types and interfaces onto valid Anythink db_type and
// display_type values. Anythink validates display_type against db_type
// server-side, so the display map is db-type-aware.
//
// Anythink valid db types:
//   varchar, varchar[], text, integer, integer[], bigint, bigint[],
//   boolean, date, timestamp, geo, decimal, decimal[], jsonb, file,
//   secret, user, one-to-{one,many}, many-to-{one,many}, dynamic-reference
//
// Display types (per db type):
//   varchar    → input | select | entity-select
//   text       → textarea | rich-text
//   boolean    → checkbox | radio
//   timestamp  → timestamp
//   date       → short-date | long-date
//   integer/bigint/decimal → input
//   jsonb      → jsonb
//   geo        → geo
//   file       → file

public static class DirectusFieldMapping
{
    public static (string DatabaseType, string DisplayType) Map(DirectusField f)
    {
        var db = MapDatabaseType(f.Type, f.Schema?.DataType);
        var display = MapDisplayType(f.Meta?.Interface, db);
        return (db, display);
    }

    public static string MapDatabaseType(string directusType, string? sqlType) =>
        directusType.ToLowerInvariant() switch
        {
            "string"     => "varchar",
            "text"       => "text",
            "integer"    => "integer",
            "biginteger" => "bigint",
            "float"      => "decimal",
            "decimal"    => "decimal",
            "boolean"    => "boolean",
            "datetime"   => "timestamp",
            "timestamp"  => "timestamp",
            "date"       => "date",
            "time"       => "varchar",     // Anythink has no "time" db type
            "json"       => "jsonb",
            "uuid"       => "varchar",
            "csv"        => "text",
            "hash"       => "varchar",
            "geometry"   => "geo",
            _            => InferFromSqlType(sqlType)
        };

    private static string InferFromSqlType(string? sqlType) =>
        sqlType?.ToLowerInvariant() switch
        {
            "int" or "int2" or "int4" or "smallint"                          => "integer",
            "int8" or "bigint"                                               => "bigint",
            "float4" or "float8" or "numeric" or "decimal" or "real" or "double" => "decimal",
            "bool" or "boolean"                                              => "boolean",
            "text" or "longtext" or "mediumtext"                             => "text",
            "json" or "jsonb"                                                 => "jsonb",
            "date"                                                           => "date",
            "datetime" or "timestamp" or "timestamptz"                       => "timestamp",
            _                                                                => "varchar"
        };

    public static string MapDisplayType(string? iface, string dbType)
    {
        var i = iface?.ToLowerInvariant();
        return dbType switch
        {
            "varchar"  => i is "select-dropdown" or "select-radio" or "select-multiple-checkbox"
                            ? "select"
                            : "input",
            "text"     => i is "input-rich-text-html" or "input-rich-text-md"
                            ? "rich-text"
                            : "textarea",
            "boolean"   => "checkbox",
            "timestamp" => "timestamp",
            "date"      => "short-date",
            "integer" or "bigint" or "decimal" => "input",
            "jsonb"     => "jsonb",
            "geo"       => "geo",
            "file"      => "file",
            _           => "input"
        };
    }
}
