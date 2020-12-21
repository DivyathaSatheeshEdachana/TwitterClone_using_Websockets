
#r "nuget: Suave"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.Remote"
#r "nuget: Akka.TestKit"
#r "nuget: FSharp.Json"
#load "./Messages.fsx"

open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Suave.ServerErrors
open Suave.Writers

open Akka.FSharp
open Akka.Actor
open Akka.Configuration
open System
open System.Net
open Messages.Messages
open System.Numerics
open System.Diagnostics
open System.Collections.Generic
open System.Threading
open System.Text.RegularExpressions
open FSharp.Json


type Message =
    | SendTweet of int * String * int
    | ParseTweet of int * String * int
    | UserTweet of int * int * int
    | Subscribe of int * int * int
    | GetFollowerList of int * int * int
    | UserList of Set<int> * int * int
    | Follow of int * int * int
    | RegisterUser of int * String * int
    | Login of int * String * int * WebSocket
    | Logout of int * int
    | HashTagQuery of String * int
    | MentionsQuery of int * int
    | GetTweetsFromId of Set<int> * int* string
    | GetFollowingTweets of int * int * string
    | SendMessageToSocket of string*int
    | NotifyUsersInternal of string*int
    
let mutable sendTweetActorRef:IActorRef = null
let mutable hashTagActorRef:IActorRef = null
let mutable userTagActorRef:IActorRef = null
let mutable userTimeLineActorRef:IActorRef = null
let mutable newsFeedActorRef:IActorRef = null
let mutable followerActorRef:IActorRef = null
let mutable registerLoginActorRef:IActorRef = null
let mutable twitterServer:IActorRef = null

let SendResponse(a, d, client) =
    let resp: PacketType= {action=a; data=d}
    registerLoginActorRef <! SendMessageToSocket(Json.serialize resp, client)
    
let registerLoginActor (mailbox: Actor<'a>) =
    
    let rec listener (regDict:  IDictionary<int,String>) (loggedInUser: IDictionary<int,WebSocket>) = 
        actor{
            let! msg = mailbox.Receive()
            match msg with
            | RegisterUser(userId,b,client) -> 
                let found, valIs = regDict.TryGetValue(userId)
                match found with
                | true -> None |> ignore
                | false -> regDict.Add(userId,b)
            | Login(userId, password,client,websocket) ->
                let found, valIs = regDict.TryGetValue(userId)
                match found with
                | true -> if valIs = password then
                              loggedInUser.Add(userId,websocket) 
                          else
                              None |> ignore
                | false -> None |> ignore
            | Logout(userId,client) ->
                loggedInUser.Remove(userId)
            | SendMessageToSocket(data, client) ->
                let found, socket = loggedInUser.TryGetValue(client)
                match found with
                | true ->
                    let byteResponse =
                        data
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment

                    Async.RunSynchronously(socket.send Text byteResponse true) |> ignore
                | false -> None |> ignore
            return! listener regDict loggedInUser
        }
    
    listener (new Dictionary<int,String>()) (new Dictionary<int,WebSocket>())
    
//TWEET ACTOR
let sendTweetActor (mailbox: Actor<'a>) =
    
    let rec listener (tweetDict: IDictionary<int,String>) (tweetID : int)=   
        actor{
            let! msg = mailbox.Receive()
            match msg with
            | SendTweet(a,b,client) ->
                tweetDict.Add(tweetID,b)
                printfn "%d %A" tweetID tweetDict.[tweetID]
                hashTagActorRef <! ParseTweet(tweetID,b,client)
                userTagActorRef <! ParseTweet(tweetID,b,client)
                userTimeLineActorRef <! UserTweet(a,tweetID,client)
                followerActorRef <! GetFollowerList(a,tweetID,client)
                followerActorRef <! NotifyUsersInternal(b,client)
                //printfn "%A" tweetDict
                return! listener tweetDict (tweetID+1)
            | GetTweetsFromId(setOfId,client, responseAction) -> 
                //printfn "============= SET OF ID %A ========== END" setOfId
                let mutable tList = []
                for x in setOfId do
                    let found, valIs = tweetDict.TryGetValue(x)
                    match found with
                    | true ->  tList <- tList @ [valIs]
                SendResponse(responseAction, Json.serialize {tweetList=tList},client)
                return! listener tweetDict tweetID
            | _ -> return! listener tweetDict tweetID
        }

    listener (new Dictionary<int, String>()) 1
    
// USER TAG DICTIONARY
let userTagActor (mailbox: Actor<'a>) =
    
    let rec listener (userTagDict: IDictionary<int,Set<int>>) =
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | ParseTweet(a,b,client) ->
                let result = b.Split ' '
                for value in result do
                    if value.StartsWith("@") then
                        let taggedUserId = value.Substring(1) |> int
                        let found, valIs = userTagDict.TryGetValue(taggedUserId)
                        match found with
                        | true -> 
                            let temp = valIs
                            userTagDict.[taggedUserId] <- temp.Add(a)
                        | false -> userTagDict.Add(taggedUserId,Set.empty.Add(a))
                // printfn "ParseTweet ======%A" userTagDict
            |MentionsQuery(user,client) ->
                //printfn "Mentions ======%A" userTagDict
                let found, valIs = userTagDict.TryGetValue(user)
                match found with
                | true ->
                        // printfn "======%A" valIs 
                        sendTweetActorRef <! GetTweetsFromId(valIs,client,"FindMeMentionedResponse")
                | false -> SendResponse("FindMeMentionedResponse", Json.serialize {tweetList=[]}, client)
            return! listener userTagDict
        }
    listener (new Dictionary<int, Set<int>>())
    
// USER TIME LINE ACTOR
let userTimeLineActor (mailbox: Actor<'a>) =
    
    let rec listener (userTimeDict: IDictionary<int,Set<int>>) =
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | UserTweet(a,b,client) ->
                let found, valIs = userTimeDict.TryGetValue(a)
                match found with
                | true -> 
                    let temp = valIs
                    userTimeDict.[a] <- temp.Add(b)
                | false -> userTimeDict.Add(a,Set.empty.Add(b))
            return! listener userTimeDict
        }
    listener (new Dictionary<int,Set<int>>())
    
// HASHTAG ACTOR
let hashTagActor (mailbox: Actor<'a>) =
    
    let rec listener (hashTagDict: IDictionary<String,Set<int>>) =
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | ParseTweet(a,b,client) ->
                //printfn "%A" b
                let result = b.Split ' '
                for value in result do
                    if value.StartsWith("#") then
                        let found, valIs = hashTagDict.TryGetValue(value)
                        match found with
                        | true -> 
                            let temp = valIs
                            hashTagDict.[value] <- temp.Add(a)
                        | false -> hashTagDict.Add(value,Set.empty.Add(a))
            |HashTagQuery(hashTag,client) ->
                let found, valIs = hashTagDict.TryGetValue(hashTag)
                match found with
                | true -> sendTweetActorRef <! GetTweetsFromId(valIs,client,"FindHashTagResponse")
                | false -> SendResponse("FindHashTagResponse",Json.serialize {tweetList=[]},client)
            //printfn "%A" hashTagDict
            return! listener hashTagDict
    }

    listener (new Dictionary<String, Set<int>>())
    
// FOLLOWER ACTOR

let followerActor (mailbox: Actor<'a>) =
    
    let rec listener (followerDict: IDictionary<int,Set<int>>) =
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | GetFollowerList(a,b,client) ->
                let found, valIs = followerDict.TryGetValue(a)
                match found with
                | true -> newsFeedActorRef <! UserList(valIs,b,client)
                | false -> None
            | NotifyUsersInternal(tweet,client) ->
                let found, valIs = followerDict.TryGetValue(client)
                match found with
                | true -> 
                    for user in valIs do
                        let actionStr = sprintf "Newtweet from %d" client
                        SendResponse(actionStr,tweet,user)
                | false -> None
            | Follow(u1,u2,client) ->
                let found, valIs = followerDict.TryGetValue(u2)
                match found with
                | true -> 
                    let temp = valIs
                    followerDict.[u2] <- temp.Add(u1)
                | false -> followerDict.Add(u2,Set.empty.Add(u1))
            return! listener followerDict
        }
    listener (new Dictionary<int, Set<int>>())
    
// NEWS FEED ACTOR

let newsFeedActor (mailbox: Actor<'a>) =
    
    let rec listener (newsFeedDict: IDictionary<int,Set<int>>) =
        actor {
            let! msg = mailbox.Receive()
            match msg with
            | UserList(a,b,client) ->
                for user in a do
                    let found, valIs = newsFeedDict.TryGetValue(user)
                    match found with
                    | true ->
                        let temp = valIs
                        newsFeedDict.[user] <- temp.Add(b)
                    | false -> newsFeedDict.Add(user,Set.empty.Add(b))
            | GetFollowingTweets(user, client, responseString) ->
                let found, valIs = newsFeedDict.TryGetValue(user)
                match found with
                | true -> sendTweetActorRef <! GetTweetsFromId(valIs,client,responseString)
                | false -> SendResponse(responseString,Json.serialize {tweetList=[]},client)
            return! listener newsFeedDict
        }
    listener (new Dictionary<int,Set<int>>())

(*
let config =
    Configuration.parse
        @"akka {
            actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            remote.helios.tcp {
                hostname = localhost
                port = 9001
            }
        }"
*)
let config = Configuration.defaultConfig()

type ServerCommand = 
        | InitialiseServer 

// global variables
let system = System.create "twitterServer" config

let serverActor (mailbox: Actor<_>) =
            let mutable globalNumberOfUsers = 0
            let rec loop () = actor {
                let! message = mailbox.Receive ()
                let packetObj = Json.deserialize<PacketType> message 
                match packetObj.action with
                | "RegisterUser" ->
                    let registerTypeObject = Json.deserialize<registerType> packetObj.data
                    let username = registerTypeObject.name
                    let password = registerTypeObject.password
                    //printfn "%A%A" (registerTypeObject.name) (registerTypeObject.password)
                    let msg = RegisterUser(username|>int, password, username|>int)
                    registerLoginActorRef <! msg
                    let response : PacketType = {action = "RegisterRequestResponse"; data = sprintf "%s registed" username}
                    //mailbox.Sender() <! (Json.serialize response)
                    SendResponse(response.action, response.data, username|>int)
                | "Logout" ->
                    let registerTypeObject = Json.deserialize<userIdFormat> packetObj.data
                    let username = registerTypeObject.name
                   // printfn "%A%A" (registerTypeObject.name) (registerTypeObject.password)
                    let msg = Logout(username|>int, username|>int)
                    registerLoginActorRef <! msg
                | "SendTweet" ->
                    let TweetTypeObject = Json.deserialize<tweetFormat> packetObj.data
                    let username = TweetTypeObject.name
                    let tweet = TweetTypeObject.tweet
                    //printfn "%A%A" (registerTypeObject.name) (registerTypeObject.password)
                    let msg = SendTweet(username|>int, tweet,username|>int)
                    sendTweetActorRef <! msg
                    let response : PacketType = {action = "SendTweetResponse"; data = sprintf "%s tweeted : %s" username tweet}
                    //mailbox.Sender() <! (Json.serialize response)
                    SendResponse(response.action, response.data, username|>int)
                | "MentionQuery" ->
                    let TweetTypeObject = Json.deserialize<userIdFormat> packetObj.data
                    let username = TweetTypeObject.name
                    //printfn "%A%A" (registerTypeObject.name) (registerTypeObject.password)
                    let msg = MentionsQuery(username|>int,username|>int)
                    userTagActorRef <! msg
                | "HashtagQuery" ->
                    let TweetTypeObject = Json.deserialize<tweetFormat> packetObj.data
                    let userid = TweetTypeObject.name
                    let hashtag = TweetTypeObject.tweet
                    //printfn "%A%A" (registerTypeObject.name) (registerTypeObject.password)
                    let msg = HashTagQuery(hashtag,userid|>int)
                    hashTagActorRef <! msg
                | "RetweetQuery" ->
                    let retweetObject = Json.deserialize<userIdFormat> packetObj.data
                    let username = retweetObject.name
                    let msg = GetFollowingTweets(username |>int,username |>int, "RetweetResponse")
                    newsFeedActorRef <! msg
                | "Subscribe" ->
                    let followObject = Json.deserialize<followFormat> packetObj.data
                    let u1 = followObject.user1
                    let u2 = followObject.user2
                    let msg = Follow(u1|>int, u2|>int,u1|>int)
                    followerActorRef <! msg
                    let response : PacketType = {action = "FollowUserResponse"; data = sprintf "%s is now following %s" u1 u2}
                    //mailbox.Sender() <! (Json.serialize response)
                    SendResponse(response.action, response.data, u1|>int)
                | "NewsFeed" ->
                    let newsFeedObj = Json.deserialize<userIdFormat> packetObj.data
                    let username = newsFeedObj.name
                    let msg = GetFollowingTweets(username |>int,username |>int, "NewsFeedResponse")
                    newsFeedActorRef <! msg
                return! loop ()
            }
            loop ()

let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    let mutable loop = true
    printfn "%A" webSocket
    while loop do
      let! msg = webSocket.read()
      printfn "connection %A" msg
      match msg with
      | (Text, data, true) ->
          let str = UTF8.toString data
          printfn "======= Received at Server %s" str
          let rObj = Json.deserialize<PacketType> str
          let registerTypeObject = Json.deserialize<registerType> rObj.data
          let username = registerTypeObject.name
          let password = registerTypeObject.password

          let msg = Login(username|>int, password, username|>int, webSocket)
          registerLoginActorRef <! msg
          let msg2 = GetFollowingTweets(username |>int,username |>int, "LoginResponse")
          newsFeedActorRef <! msg2
      | (Close, _, _) ->
        printfn "C%A" Close
        loop <- false

      | _ -> printfn "Matched Nothing %A" msg
    }

twitterServer <- spawn system "server" serverActor
sendTweetActorRef <- spawn system "peer0" (sendTweetActor)
hashTagActorRef <- spawn system "hashActor" (hashTagActor)
userTagActorRef <- spawn system "userTagActor" (userTagActor)
userTimeLineActorRef <- spawn system "userTimeActor" (userTimeLineActor)
newsFeedActorRef <- spawn system "newsFeedActor" (newsFeedActor)
followerActorRef <- spawn system "followerActor" (followerActor)
registerLoginActorRef <- spawn system "regLoginActor" (registerLoginActor)
twitterServer <! ServerCommand.InitialiseServer

let processApi (rawForm: byte[]) =
    let message = System.Text.Encoding.UTF8.GetString(rawForm)
    printfn "%s" message
    twitterServer <! message |>ignore
    message

let handleRequest = 
    request (fun req ->
    req.rawForm
    |> processApi
    |> OK)
    >=> setMimeType "application/json"  

let app : WebPart = 
  choose [
    path "/" >=> OK "Hello"
    path "/websocket" >=> handShake ws
    path "/api" >=> handleRequest
    NOT_FOUND "Found no handlers." 
    ]

startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app