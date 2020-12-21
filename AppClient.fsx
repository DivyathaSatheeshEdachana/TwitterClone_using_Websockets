
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: FSharp.Json"
#r "nuget: FSharp.Data"
#load "./Messages.fsx"

open System
open System.IO
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

open Akka.FSharp
open Akka.Actor
open Akka.Configuration
open FSharp.Json
open System.Collections.Generic
open Messages.Messages

let ws = new ClientWebSocket()
let uri = new Uri("ws://localhost:8080/websocket")
let cts = new CancellationToken()
let ctask = ws.ConnectAsync(uri,cts)
while not (ctask.IsCompleted) do
    ()

let rec receive() =
    async{
        let rsegment = new ArraySegment<Byte>(Array.create(500) Byte.MinValue)
        let task = ws.ReceiveAsync(rsegment, cts)
        while not (task.IsCompleted) do
            ()
        let resp = System.Text.Encoding.ASCII.GetString (rsegment.Array)
        printfn "\n================ Received Response %s" resp
        return! receive()
    }

Async.Start(receive(), (new CancellationTokenSource()).Token)

let mutable loop = true
let mutable enteredId = false
let mutable userId = ""


let sendapi data =
    let json = data
    Http.Request("http://127.0.0.1:8080/api",httpMethod = "POST",headers = [ ContentType HttpContentTypes.Json ],body = TextRequest json) |> ignore

let sendlogin (data:string) =
    let buffer = System.Text.Encoding.ASCII.GetBytes data
    let segment = new ArraySegment<byte>(buffer)
    ws.SendAsync(segment, WebSocketMessageType.Text, true, cts) |> ignore

(*
Register|{username}|{password}
Login|{username}|{password}
Logout
Retweet
Newsfeed
Tweet|{tweet}
Follow|{user}
Search|Mentions
Search|Hashtag|{hashtag}
*)

while loop do
    printfn "Input Command"
    let message = Console.ReadLine()
    let command = message.Split('|')
    if (command.[0] ="exit") then
        loop <- false

    elif (command.[0] = "Register") then
        let user = command.[1]
        let pwd = command.[2]
        let data : registerType = {
            name = user
            password = pwd
        }
        let request : PacketType = {action = "RegisterUser"; data = Json.serialize data }
        userId <- user 
        sendapi (Json.serialize request)

    elif (command.[0] = "Login") then
        let user = command.[1]
        let pwd = command.[2]
        let data : registerType = {
            name = user
            password = pwd
        }
        let request : PacketType = {action = "LoginUser"; data = Json.serialize data }
        userId <- user 
        sendlogin (Json.serialize request)

    elif (command.[0] = "Logout") then
        let data = {
            name = userId
        }
        let request : PacketType = {action = "Logout"; data = Json.serialize data }
        sendapi (Json.serialize request)

    elif (command.[0] = "Retweet") then
        let data = {
            name = userId
        }
        let request : PacketType = {action = "RetweetQuery"; data = Json.serialize data }
        sendapi (Json.serialize request)

    elif (command.[0] = "Newsfeed") then
        let data = {
            name = userId
        }
        let request : PacketType = {action = "NewsFeed"; data = Json.serialize data }
        sendapi (Json.serialize request)

    elif (command.[0] = "Tweet") then
        let tweetData : tweetFormat = {
            name = userId
            tweet = command.[1]
        }
        let request : PacketType = {action = "SendTweet"; data = Json.serialize tweetData }
        sendapi (Json.serialize request)

    elif (command.[0] = "Follow") then
        sendapi (Json.serialize {action = "Subscribe"; data = Json.serialize {user1=userId; user2=command.[1]} })

    elif (command.[0] = "Search" && command.[1] = "Mentions") then
        let request : PacketType = {action = "MentionQuery"; data = Json.serialize {name=userId} }
        sendapi (Json.serialize request)

    elif (command.[0] = "Search" && command.[1] = "Hashtag") then
        let request : PacketType = {action = "HashtagQuery"; data = Json.serialize {name=userId; tweet=command.[2]} }
        sendapi (Json.serialize request)

    else
        printfn "%s" "Register|{username}|{password}
        Login|{username}|{password}
        Logout
        Retweet
        Newsfeed
        Tweet|{tweet}
        Follow|{user}
        Search|Mentions
        Search|Hashtag|{hashtag}"
    ()