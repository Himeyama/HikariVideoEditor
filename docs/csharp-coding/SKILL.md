---
name: csharp-coding
description: .NET 8+ / C# 12+ のコーディング規約。明示的型・nullable・パターンマッチング・LINQ・async など。
---

# C# Coding Guidelines

Target: .NET 8+ / C# 12+.

## Variable Declarations

Never use `var`. Write the type on the left; use `new()` (target-typed new) to avoid repeating it on the right.

```csharp
User user = new();
List<string> names = new();
Dictionary<string, int> dict = new();
List<User> active = users.Where(u => u.IsActive).ToList();
string result = GetValue();
```

## Type System

### Nullable reference types

Enable in the csproj (`<Nullable>enable</Nullable>`), then `?` marks what may be null.

```csharp
string name = "abc";          // non-nullable
string? maybe = null;         // nullable
string display = maybe ?? "anonymous";  // null-coalescing
int? length = maybe?.Length;            // null-conditional
```

### Collection expressions

```csharp
List<int> numbers = [1, 2, 3];
int[] values = [1, 2, 3];
string[] empty = [];
int[] merged = [..a, ..b];   // spread
```

### Pattern matching

```csharp
string message = code switch
{
    200 => "OK",
    404 => "Not Found",
    >= 500 => "Server Error",
    _ => "Unknown"
};

// type + property patterns
string Describe(object obj) => obj switch
{
    int n when n > 0 => "positive int",
    string s => $"string: {s}",
    null => "null",
    _ => "other"
};

if (user is { IsActive: true, Role: "admin" }) { ... }
```

## Type Definitions

Use file-scoped namespaces.

```csharp
namespace MyApp.Services;

class UserService { }
```

### record

Use `record` for data that needs value equality; copy with `with`.

```csharp
record User(string Name, int Age);

User updated = user with { Age = 30 };
```

### init-only / required

```csharp
public class User
{
    public required string Name { get; init; }
    public int Age { get; init; } = 0;
}

User user = new() { Name = "Hikari" };  // Name is mandatory
```

### Primary constructors

Inject dependencies directly into the class header (C# 12).

```csharp
class UserService(IUserRepository repo, ILogger<UserService> logger)
{
    public async Task<User?> GetAsync(int id)
    {
        logger.LogInformation("Getting user {Id}", id);
        return await repo.FindAsync(id);
    }
}
```

## LINQ

Prefer method syntax — usually more readable than query syntax.

```csharp
List<User> adults = users
    .Where(u => u.Age >= 18)
    .OrderBy(u => u.Name)
    .ToList();

int count = users.Count(u => u.IsActive);
User? first = users.FirstOrDefault(u => u.Id == id);
```

## Async

Return `Task` (no result) or `Task<T>`; never block the caller.

```csharp
async Task SaveAsync(User user) { ... }
async Task<User?> FindAsync(int id) { ... }

User? user = await repo.FindAsync(id);

// run in parallel
(ResultA a, ResultB b) = await (TaskA(), TaskB());
await Task.WhenAll(task1, task2);
```

Use `ConfigureAwait(false)` in library code (no need to resume on the caller's context).

```csharp
string data = await httpClient.GetStringAsync(url).ConfigureAwait(false);
```

## Resource Disposal

`using` declarations dispose at end of scope.

```csharp
using StreamReader file = new(path);
using SqlConnection conn = new(connStr);
```

## Minimal API

```csharp
WebApplication app = WebApplication.Create(args);

app.MapGet("/users/{id}", async (int id, IUserService svc) =>
{
    User? user = await svc.FindAsync(id);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

app.Run();
```

Return `Results` factories, not raw objects: `Results.Ok(user)`, `Results.NotFound()`, `Results.BadRequest("Invalid input")`, `Results.Created($"/users/{user.Id}", user)`.

## Error Handling

Don't use exceptions for normal control flow — use the `TryParse` pattern instead.

```csharp
if (int.TryParse(s, out int value)) { ... }   // not try { int.Parse(s) } catch
```

Represent expected failures as return values.

```csharp
record Result<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;
    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(string error) => new(default, error);
}
```

## Misc

```csharp
string msg = $"Hello, {name}!";   // interpolation

// raw string literals (C# 11) for multi-line / embedded quotes
string json = """
    {
        "name": "Hikari"
    }
    """;

const int MaxRetry = 3;            // compile-time constant
readonly List<string> _tags = [];  // immutable after construction
```

## Naming

| Target | Convention | Example |
|---|---|---|
| Class / method / property | `PascalCase` | `UserService`, `GetAsync` |
| Local variable / parameter | `camelCase` | `userId`, `isActive` |
| Private field | `_camelCase` | `_repository` |
| Constant | `PascalCase` | `MaxRetry` |
| Interface | `I` prefix | `IUserRepository` |
