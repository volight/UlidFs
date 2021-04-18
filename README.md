# UlidFs

[![.NET](https://github.com/volight/UlidFs/actions/workflows/dotnet.yml/badge.svg)](https://github.com/volight/UlidFs/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/UlidFs?label=NuGet)](https://www.nuget.org/packages/UlidFs/)
[![MIT](https://img.shields.io/github/license/volight/UlidFs)](https://github.com/volight/UlidFs/blob/master/LICENSE)
[![binary](https://img.shields.io/badge/ULID-Binary_Impl-blueviolet)](https://github.com/ulid/spec)

Ulid implementation in F#

## Usage

```fs
open Volight.Ulid

let id = Ulid.NewUlid()
let id = ulid()
let str = id.ToString()
let guid = id.ToGuid()
let id = Ulid.Parse(str)
let id = Ulid(str)
let success = Ulid.TryParse(str, &id)

let id = Slid.NewSlid()
let id = slid()
let str = id.ToString()
let id = Slid.Parse(str)
let id = Slid(str)
let success = Slid.TryParse(str, &id)
```

### Slid 
Short version of Ulid (x64)

- layout 
    ```
    rrr             tttttttttt

    |-|           |-------------|
    Randomness       Timestamp
    16bits             48bits
    3 characters   10 characters
    |---------------------------|
                64bits
             13 characters
    ```

## Other

[spec](https://github.com/ulid/spec)  
