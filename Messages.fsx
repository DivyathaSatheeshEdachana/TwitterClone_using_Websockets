#r "nuget: FSharp.Json"

open FSharp.Json


module Messages = 

    type PacketType = {
        action : string
        data : string
    }

    type registerType = {
        name : string
        password :string
    }
    
    type tweetFormat = {
        name : string
        tweet : string
    }
    
    type userIdFormat = {
        name : string
    }

    type tweetListFormat = {
        tweetList: string list
    }

    type followFormat = {
        user1: string
        user2: string
    }

    type LoginType = {
        name : string
        password :string
    }

    type LogoutType = {
        name : string
    }