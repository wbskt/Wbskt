namespace Webskt.Workflow.Abstraction.Enums;

public enum FunctionName
{
    // Date/Time
    Now = 0,
    UtcNow = 1,
    FormatDate = 2,
    AddDays = 3,

    // Math
    Round = 10,
    Floor = 11,
    Ceiling = 12,
    Abs = 13,
    Min = 14,
    Max = 15,

    // String
    Upper = 20,
    Lower = 21,
    Trim = 22,
    Concat = 23,
    Contains = 24, // Function version: CONTAINS(text, search)
    Replace = 25,

    // Collection/Array
    Count = 30,
    First = 31,
    Last = 32,
    Any = 33,
    All = 34,

    // Logic/Data
    Coalesce = 40, // Returns first non-null
    IsMatch = 41   // Regex
}
