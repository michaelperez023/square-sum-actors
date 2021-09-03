//#time "on"
#r "nuget: Akka.FSharp" 
//#r "nuget: Akka.TestKit" 

//open System
open Akka.Actor
//open Akka.Configuration
//open Akka.FSharp
//open Akka.TestKit

let max = string (fsi.CommandLineArgs.GetValue 1)
let length = string (fsi.CommandLineArgs.GetValue 2)
let system = ActorSystem.Create("System")

//type EchoServer =
//    inherit Actor
//    override x.OnReceive message =
//        match message with
//        | :? string as msg -> printfn "Hello %s" msg
//        | _ ->  failwith "unknown message"
//let echoServer = system.ActorOf(Props(typedefof<EchoServer>, Array.empty))

printfn "max: %s" max
printfn "length: %s" length

