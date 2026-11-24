# Column reinforcement table parameters

The `BS_Rebar_Detail 2D` table previously used exact instance parameter names.
The error `Family bảng thiếu Property ghi được` did not distinguish an absent
parameter from a read-only/formula parameter.

Parameter lookup now prefers exact names, then matches case-insensitively while
ignoring whitespace and underscores. For example `Bar Diameter_StirrupTop&Bot`
matches `Bar Diameter_Stirrup Top&Bot`. End and middle ties remain separate;
ambiguous matches are rejected rather than selecting an arbitrary parameter.

Both instance and type parameters are supported. Tables requiring type data get
a private duplicated type before writing, leaving existing instances unchanged.
Number values stay in millimetres as numeric labels; Length values are converted
to internal Revit units. Integer and numeric Text parameters are supported.
Read-only values are accepted only when they already match after regeneration;
otherwise the message identifies a formula/read-only problem. Missing fields
report relevant available family parameter names.

Validation: `dotnet run --project tests/ColumnRebarLayout/ColumnRebarLayout.csproj -c Release`
includes name/ambiguity regressions. Build with `dotnet build KStruct.csproj -c Debug`.
Revit validation remains required with the user's actual family: spacing variant,
instance/type data, Number versus Length, formula mismatch, two tables with
different reinforcement, and Undo without modifying pre-existing tables.
