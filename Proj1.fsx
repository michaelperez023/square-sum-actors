#time "on"
#r "nuget: Akka.FSharp" 

open System
open Akka.Actor
open Akka.FSharp

type Input = Input of int * int

let isSumOfSquaresPerfectSquare (Input(x, k)) : bool = 
    let square x = x * x
    let isPerfectSquare (x : int) : bool =
        let y = round (sqrt (double x)) |> uint64
        y * y = uint64 x
    [ x .. x + k - 1 ] |> List.map square |> List.sum |> isPerfectSquare

let WorkerActor (mailbox:Actor<_>) = 
    let rec loop () = actor {
        let! message = mailbox.Receive()

        //match message with
        //| Input(n, k) ->
        //    printfn "processor received input %i and %i." n k
        //| _ -> ()
        return! loop ()
    }
    loop ()

let BossActor (mailbox:Actor<_>) = 
    let rec loop () = actor {
        let! message = mailbox.Receive()

        match message with
        | Input(n, k) ->
            printfn "processor received input %i and %i." n k
            let check = Input(n, k) |> isSumOfSquaresPerfectSquare
            if check then printfn "true" else printfn "false"
        | _ -> ()
        return! loop ()
    }
    loop ()

match fsi.CommandLineArgs.Length with 
| 3 ->
    let max_n = fsi.CommandLineArgs.[1] |> int
    let length_k = fsi.CommandLineArgs.[2] |> int

    let system = ActorSystem.Create("System")
    let bossRef = spawn system "boss" BossActor
    bossRef <! Input(max_n, length_k)

    ()
| _ -> failwith "You must enter two numbers as input. i.e. 'dotnet fsi proj1.fsx 3 2'"
