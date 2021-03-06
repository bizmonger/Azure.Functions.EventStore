﻿namespace Azure

open System
open Microsoft.WindowsAzure.Storage
open Azure.Entities
open Azure.Table
open EventStore.Core.Language
open EventStore.Operations
open EventStore.Language
open EventStore.Utilities

module EventStore =

    let private streamTable = "Stream"

    let tryConnect : Create =
    
        fun request -> 
            
            async {
                
                try
                    let storageAccount   = CloudStorageAccount.Parse request.ConnectionString
                    let cloudTableClient = storageAccount.CreateCloudTableClient()
    
                    if obj.ReferenceEquals(cloudTableClient, null) then
                        return Error "Connection failed" 
    
                    else 
                        let result : Connection = { 
                            Context          = cloudTableClient
                            ConnectionString = request.ConnectionString
                        }
    
                        return Ok result
    
                with ex -> return Error <| ex.GetBaseException().Message
            }

    let tryTerminate : Terminate =
    
        fun connection ->
    
            async {
    
                try
                    // Not clear if anything needs to be done with the following:
                    // connection.Context :?> CloudStorageAccount
                    return Ok ()
    
                with ex -> return Error <| ex.GetBaseException().Message
                
            }

    let tryAppend (event:Event) (connectionstring:ConnectionString) =

        async {
    
            try
                let partitionKey = PartitionKey <| (valueFromStreamId event.Stream)
                let rowKey       = RowKey       <| Guid.NewGuid().ToString()

                let entity = EventEntity()
                entity.RowKey       <- rowKey          |> valueFromRowKey
                entity.PartitionKey <- partitionKey    |> valueFromPartitionKey
                entity.Stream       <- event.Stream    |> valueFromStreamId
                entity.Data         <- event.Data      |> valueFromData
                entity.MetaData     <- event.MetaData  |> valueFromMeta
                entity.EventType    <- event.EventType |> valueFromEventType

                match! (connectionstring, streamTable) ||> tryCreate entity with
                | Error msg -> return Result.Error <| msg
                | Ok _      -> return Result.Ok    <| valueFromRowKey rowKey 

            with ex -> return Error <| ex.GetBaseException().Message
        }

    let tryAppendMultiple (events:Event seq) (connectionstring:ConnectionString) =

        async {

            try
                let isSuccessful =  function
                    | Error _ -> false
                    | Ok    _ -> true
    
                return
                    events 
                    |> Seq.map (fun event -> async { return! tryAppend event connectionstring } |> Async.RunSynchronously)
                    |> Seq.forall isSuccessful
                    |> function
                        | false -> Result.Error "Failed to create an event"
                        | true  -> Result.Ok ()

            with ex -> return Error <| ex.GetBaseException().Message
        }

    let tryReadBackwards (Stream stream) (connectionstring:ConnectionString) : AsyncResult<Event seq, ErrorDescription> =

        async {

            let partitionKey, table = PartitionKey stream, Table streamTable

            match! connectionstring |> tryReadBackwards<EventEntity> partitionKey table with
            | Error msg   -> return Error msg
            | Ok entities -> return Ok (entities |> Seq.map toEvent)
        }

    let tryReadBackwardsCount (Stream stream) (count:int) (connectionstring:ConnectionString) : AsyncResult<Event seq, ErrorDescription> =

        async {

            let partitionKey, table = PartitionKey stream, Table streamTable

            match! connectionstring |> tryReadBackwardsCount<EventEntity> table partitionKey count with
            | Error msg   -> return Error msg
            | Ok entities -> return Ok (entities |> Seq.map toEvent)
        }

    let tryReadLastEvent (Stream stream) (connectionstring:ConnectionString) : AsyncResult<Event seq, ErrorDescription> =

        async {

            let partitionKey, table = PartitionKey stream, Table streamTable

            match! connectionstring |> Table.tryReadBackwardsCount<EventEntity> table partitionKey 1 with
            | Error msg   -> return Error msg
            | Ok entities -> return Ok (entities |> Seq.map toEvent)
        }