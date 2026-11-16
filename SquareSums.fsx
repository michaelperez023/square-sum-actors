// Finds every start s in 1..N for which s^2 + (s+1)^2 + ... + (s+k-1)^2 is a perfect square,
// with a boss actor that hands out work units to worker actors (Akka.NET).
// Author: Michael Perez - COP 5615 Distributed Operating System Principles - Project 1
//
// Usage: dotnet fsi SquareSums.fsx <N> <k> [--workers <w>] [--unit <u>] [--verify] [--stats]
//   N            largest start to check
//   k            number of consecutive squares in each sum
//   --workers w  number of worker actors (default: one per processor)
//   --unit u     starts per work unit (default 100000)
//   --verify     re-check every printed start with BigInteger arithmetic and, when N <= 1000000,
//                compare the output with a sequential scan of every start (exit code 1 if anything is wrong)
//   --stats      print the workers, unit size, real time, CPU time, CPU/real ratio and the time the
//                workers spent on their units to stderr
//
// The starts are printed to stdout in ascending order, one per line, and nothing else is: statistics,
// verification results, errors and Akka.NET's log messages go to stderr.

#r "nuget: Akka.FSharp, 1.5.74"

open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Numerics
open System.Text
open System.Threading.Tasks
open Akka.Actor
open Akka.FSharp

// ---------------------------------------------------------------------------
// Exact arithmetic
//
// Every sum fits in a UInt128: the program refuses N and k whose largest sum, N^2 + ... + (N+k-1)^2,
// is 2^128 or more. Two details keep the hot loop fast:
// - F# compares UInt128 values with its generic comparison, which boxes them, so the loop calls UInt128's
//   own operators (op_GreaterThan, Equals) instead of >, <= and =.
// - isqrt and isSquare are inline, so the whole check is one loop in findStarts. The .NET JIT starts
//   every method unoptimized and optimizes a running loop early (on-stack replacement), but small
//   functions called from the loop would stay unoptimized until much later.

/// The window sum S(s) = s^2 + (s+1)^2 + ... + (s+k-1)^2 = k*s^2 + k*(k-1)*s + (k-1)*k*(2k-1)/6, as a BigInteger.
let windowSum (k: int64) (s: int64) =
    let k, s = bigint k, bigint s
    k * s * s + k * (k - 1I) * s + (k - 1I) * k * (2I * k - 1I) / 6I

let largestSum = (1I <<< 128) - 1I

/// r * r as a 128-bit number.
let inline square (r: uint64) = UInt128(0UL, r) * UInt128(0UL, r)

/// floor(sqrt n), exactly, for every 128-bit n.
let inline isqrt (n: UInt128) : uint64 =
    // The floating-point root has a relative error of about 2^-53, so for n < 2^104 it is within one
    // of the true root.
    let estimate = Math.Sqrt(double n)
    let mutable r = if estimate >= 18446744073709551616.0 (* 2^64 *) then UInt64.MaxValue else uint64 estimate

    if r > (1UL <<< 52) then
        // Larger n: one integer Newton step from the estimate gives the root or one more than it.
        let wide = UInt128(0UL, r)
        r <- uint64 (UInt128.Min((wide + n / wide) >>> 1, UInt128(0UL, UInt64.MaxValue)))

    while UInt128.op_GreaterThan(square r, n) do
        r <- r - 1UL

    while r < UInt64.MaxValue && UInt128.op_LessThanOrEqual(square (r + 1UL), n) do
        r <- r + 1UL

    r

/// Bit i is set when i is a square modulo 64. The last six bits of a square are one of these 12
/// patterns, so many sums are rejected before any square root is taken. How many depends on k:
/// half of them for k = 24, all for k = 4, seven in eight for k = 409, none for k = 1.
let squaresMod64 =
    Seq.fold (fun mask x -> mask ||| (1UL <<< (x * x % 64))) 0UL [ 0..63 ]

let inline isSquare (n: UInt128) =
    (squaresMod64 >>> int (uint64 n &&& 63UL)) &&& 1UL <> 0UL
    && (square (isqrt n)).Equals n

/// The starts s in first..last (ascending) whose window of k squares sums to a perfect square.
let findStarts (k: int64) (first: int64) (last: int64) =
    let found = ResizeArray<int64>()
    let twoK = UInt128(0UL, uint64 k) * UInt128(0UL, 2UL)
    let kSquared = square (uint64 k)
    // S(first - 1) from the closed form (S(0) = 0^2 + ... + (k-1)^2), then each step slides the window:
    // S(s+1) = S(s) + (s+k)^2 - s^2 = S(s) + 2ks + k^2.
    let mutable sum: UInt128 = BigInteger.op_Explicit (windowSum k (first - 1L))
    let mutable s = first - 1L

    while s < last do
        sum <- sum + twoK * UInt128(0UL, uint64 s) + kSquared
        s <- s + 1L

        if isSquare sum then
            found.Add s

    found.ToArray()

// ---------------------------------------------------------------------------
// Checks for --verify: BigInteger arithmetic, square pyramids instead of windowSum, no floating point

/// 1^2 + 2^2 + ... + m^2.
let pyramid (m: bigint) = m * (m + 1I) * (2I * m + 1I) / 6I

/// floor(sqrt n) by Newton's method, starting from a power of two above the root.
let isqrtBig (n: bigint) =
    if n < 2I then
        n
    else
        let mutable x = 1I <<< ((int (n.GetBitLength()) + 1) / 2)
        let mutable y = (x + n / x) / 2I

        while y < x do
            x <- y
            y <- (x + n / x) / 2I

        x

/// Whether s^2 + ... + (s+k-1)^2, taken as the difference of two square pyramids, is a perfect square.
let isSolution (k: int64) (s: int64) =
    let total = pyramid (bigint s + bigint k - 1I) - pyramid (bigint s - 1I)
    let root = isqrtBig total
    root * root = total

let sequentialScanLimit = 1_000_000L

/// Problems with the printed starts or with the 128-bit square root, or an empty list.
let verify (n: int64) (k: int64) (printed: int64[]) =
    let problems = ResizeArray<string>()

    let checkSquareRoot (value: bigint) =
        let exact = isqrtBig value
        let wide: UInt128 = BigInteger.op_Explicit value

        if BigInteger(isqrt wide) <> exact || isSquare wide <> (exact * exact = value) then
            problems.Add $"the 128-bit square root is wrong for {value}"

    // isqrt and isSquare on squares and their neighbours of every size up to 2^128 - 1, since most runs
    // never reach sums that large: for each bit length, the largest root and 16 random ones.
    let random = Random(1)
    checkSquareRoot largestSum

    for bits in 1..64 do
        let smallest = 1I <<< (bits - 1)

        let roots =
            ((smallest <<< 1) - 1I)
            :: [ for _ in 1..16 -> smallest + bigint (random.NextInt64()) * bigint (random.NextInt64()) % smallest ]

        for root in roots do
            for value in [ root * root - 1I; root * root; root * root + 1I ] do
                checkSquareRoot value

    for i in 1 .. printed.Length - 1 do
        if printed.[i] <= printed.[i - 1] then
            problems.Add $"{printed.[i]} is printed after {printed.[i - 1]}"

    for s in printed do
        if s < 1L || s > n then
            problems.Add $"{s} is outside 1..{n}"
        elif not (isSolution k s) then
            problems.Add $"the {k} squares from {s}^2 do not sum to a perfect square"

    if n <= sequentialScanLimit then
        let expected = HashSet<int64>(seq { 1L .. n } |> Seq.filter (isSolution k))
        let seen = HashSet<int64>(printed)

        for s in Seq.sort expected do
            if not (seen.Contains s) then
                problems.Add $"{s} is missing"

    List.ofSeq problems

// ---------------------------------------------------------------------------
// Actors

/// Starts first..last, numbered by Index so the boss can print the units in order.
type Work = { Index: int64; First: int64; Last: int64 }

type BossMessage =
    | Start
    | UnitDone of index: int64 * starts: int64[] * busy: TimeSpan
    | UnitFailed of index: int64 * error: string

type Outcome =
    | Completed of printed: ResizeArray<int64> * busy: TimeSpan
    | Failed of string

type Options =
    { N: int64
      K: int64
      Workers: int
      Unit: int64
      Verify: bool
      Stats: bool }

/// The number of units of options.Unit starts that cover 1..N.
let unitCount (options: Options) = (options.N - 1L) / options.Unit + 1L

/// No more workers than units.
let workerCount (options: Options) =
    int (min (int64 options.Workers) (unitCount options))

let workerActor (k: int64) (mailbox: Actor<Work>) =
    let rec loop () =
        actor {
            let! work = mailbox.Receive()
            let started = Stopwatch.GetTimestamp()

            let reply =
                try
                    let starts = findStarts k work.First work.Last
                    UnitDone(work.Index, starts, Stopwatch.GetElapsedTime started)
                with e ->
                    UnitFailed(work.Index, e.Message)

            mailbox.Sender() <! reply
            return! loop ()
        }

    loop ()

/// Splits 1..N into units of options.Unit starts, keeps every worker supplied with units, and writes the
/// starts each unit found to out, in unit order. Completes finished when all units are done.
let bossActor (options: Options) (out: TextWriter) (finished: TaskCompletionSource<Outcome>) (mailbox: Actor<BossMessage>) =
    let units = unitCount options
    let mutable nextUnit = 0L
    let mutable nextToPrint = 0L
    let waiting = Dictionary<int64, int64[]>() // finished units that wait for an earlier unit
    let printed = ResizeArray<int64>() // kept for --verify
    let mutable busy = TimeSpan.Zero // time the workers spent on their units, added up

    let sendWork (worker: IActorRef) =
        if nextUnit < units then
            let first = nextUnit * options.Unit + 1L
            let last = first + min (options.Unit - 1L) (options.N - first)
            worker <! { Index = nextUnit; First = first; Last = last }
            nextUnit <- nextUnit + 1L

    let handle message =
        match message with
        | Start ->
            let pool =
                [ for i in 1 .. workerCount options -> spawn mailbox $"worker{i}" (workerActor options.K) ]

            // Two units each: a worker that finishes a unit starts on its second one while the boss
            // sends it the next.
            for _ in 1..2 do
                for worker in pool do
                    sendWork worker

        | UnitDone(index, starts, unitBusy) ->
            sendWork (mailbox.Sender())
            busy <- busy + unitBusy
            waiting.[index] <- starts

            while waiting.ContainsKey nextToPrint do
                for s in waiting.[nextToPrint] do
                    out.WriteLine s

                    if options.Verify then
                        printed.Add s

                waiting.Remove nextToPrint |> ignore
                nextToPrint <- nextToPrint + 1L

            if nextToPrint = units then
                out.Flush()
                finished.TrySetResult(Completed(printed, busy)) |> ignore

        | UnitFailed(index, error) -> finished.TrySetResult(Failed $"unit {index} failed: {error}") |> ignore

    let rec loop () =
        actor {
            let! message = mailbox.Receive()

            try
                handle message
            with e ->
                finished.TrySetResult(Failed e.Message) |> ignore

            return! loop ()
        }

    loop ()

// ---------------------------------------------------------------------------
// Main

let usage =
    "usage: dotnet fsi SquareSums.fsx <N> <k> [--workers <w>] [--unit <u>] [--verify] [--stats]"

let maxWorkers = 1000

let tryInt64 (text: string) =
    match Int64.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture) with
    | true, value when value >= 1L -> Some value
    | _ -> None

let rec parseOptions (options: Options) args =
    match args with
    | [] -> Ok options
    | "--verify" :: rest -> parseOptions { options with Verify = true } rest
    | "--stats" :: rest -> parseOptions { options with Stats = true } rest
    | "--workers" :: value :: rest ->
        match tryInt64 value with
        | Some w when w <= int64 maxWorkers -> parseOptions { options with Workers = int w } rest
        | _ -> Error $"--workers must be a whole number from 1 to {maxWorkers}, not \"{value}\""
    | "--unit" :: value :: rest ->
        match tryInt64 value with
        | Some u -> parseOptions { options with Unit = u } rest
        | None -> Error $"--unit must be a whole number of at least 1, not \"{value}\""
    | option :: _ -> Error $"unknown or incomplete option \"{option}\""

let parseArguments (args: string list) =
    match args with
    | n :: k :: rest ->
        match tryInt64 n, tryInt64 k with
        | Some n, Some k ->
            let defaults =
                { N = n
                  K = k
                  Workers = min Environment.ProcessorCount maxWorkers
                  Unit = 100_000L
                  Verify = false
                  Stats = false }

            parseOptions defaults rest
        | _ -> Error $"N and k must be whole numbers from 1 to {Int64.MaxValue}"
    | _ -> Error "N and k are required"

/// The largest x in 1..Int64.MaxValue for which fits x holds, or 0 if fits 1 does not. fits must hold up
/// to some x and not after it.
let largestFitting (fits: int64 -> bool) =
    if fits Int64.MaxValue then
        Int64.MaxValue
    else
        // fits low (or low = 0), and not (fits high)
        let mutable low, high = 0L, Int64.MaxValue

        while high - low > 1L do
            let middle = low + (high - low) / 2L
            if fits middle then low <- middle else high <- middle

        low

match parseArguments (List.tail (List.ofArray fsi.CommandLineArgs)) with
| Error message ->
    eprintfn "error: %s" message
    eprintfn "%s" usage
    exit 2
| Ok options when windowSum options.K options.N > largestSum ->
    let limit =
        match largestFitting (fun n -> windowSum options.K n <= largestSum) with
        | 0L -> $"no N works with this k, which can be at most {largestFitting (fun k -> windowSum k 1L <= largestSum)}"
        | n -> $"with k = {options.K}, N can be at most {n}"

    eprintfn
        "error: for N = %d and k = %d the largest sum, %d^2 + ... + %O^2, is 2^128 or more, too large for 128-bit arithmetic; %s"
        options.N
        options.K
        options.N
        (bigint options.N + bigint options.K - 1I)
        limit

    exit 2
| Ok options ->
    let config =
        Akka.Configuration.ConfigurationFactory.ParseString
            """
            akka {
                loglevel = WARNING
                log-dead-letters = off
                log-dead-letters-during-shutdown = off
            }
            """

    // The starts go to the stdout stream through out. Everything written to Console.Out, such as Akka.NET's
    // log messages, goes to stderr.
    let out = new StreamWriter(Console.OpenStandardOutput(), UTF8Encoding(false), 1 <<< 16)
    out.NewLine <- "\n"
    Console.SetOut Console.Error

    let system = ActorSystem.Create("squaresums", config)
    let finished = TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously)

    // A worker that fails outside a unit is stopped and ends the run. Akka.NET's default would restart it
    // without the unit it was working on, and the boss would wait for that unit forever.
    let stopOnFailure =
        Strategy.OneForOne(fun e ->
            finished.TrySetResult(Failed $"a worker failed: {e.Message}") |> ignore
            Directive.Stop)

    let cpuBefore = Process.GetCurrentProcess().TotalProcessorTime
    let clock = Stopwatch.StartNew()
    let boss = spawnOpt system "boss" (bossActor options out finished) [ SpawnOption.SupervisorStrategy stopOnFailure ]
    boss <! Start
    let outcome = finished.Task.Result
    let real = clock.Elapsed.TotalSeconds
    let cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds
    system.Terminate().Wait()

    let count (n: int64) word = if n = 1L then $"1 {word}" else $"{n} {word}s"

    let printStats (busy: TimeSpan option) =
        eprintfn "%s, %s of up to %s" (count (int64 (workerCount options)) "worker") (count (unitCount options) "unit") (count options.Unit "start")
        eprintfn "real %.3f s, CPU %.3f s, CPU/real %.2f" real cpu (cpu / real)

        match busy with
        | Some busy -> eprintfn "workers busy %.3f s, busy/real %.2f" busy.TotalSeconds (busy.TotalSeconds / real)
        | None -> ()

    match outcome with
    | Failed error ->
        eprintfn "error: %s" error
        printStats None
        exit 1
    | Completed(printed, busy) ->
        if options.Stats then
            printStats (Some busy)

        if options.Verify then
            match verify options.N options.K (printed.ToArray()) with
            | [] when options.N <= sequentialScanLimit ->
                eprintfn
                    "verify: OK, %s re-checked with BigInteger arithmetic, and a sequential scan of 1..%d finds the same starts"
                    (count (int64 printed.Count) "start")
                    options.N
            | [] ->
                eprintfn
                    "verify: OK, %s re-checked with BigInteger arithmetic (no sequential scan for N > %d)"
                    (count (int64 printed.Count) "start")
                    sequentialScanLimit
            | problems ->
                for problem in List.truncate 20 problems do
                    eprintfn "verify: %s" problem

                eprintfn "verify: FAILED (%s)" (count (int64 problems.Length) "problem")

                if not options.Stats then
                    printStats (Some busy)

                exit 1

        exit 0
