﻿namespace Azure

open System.Net
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table

module Storage =

    type ConnectionString = ConnectionString of string
    type Table            = Table            of string
    type PartitionKey     = PartitionKey     of string
    type RowKey           = RowKey           of string

    let exists (ConnectionString connectionString) (Table tableName) =

        async {
        
            try
                let storageAccount   = CloudStorageAccount.Parse connectionString
                let cloudTableClient = storageAccount.CreateCloudTableClient()
                let cloudTable       = cloudTableClient.GetTableReference(tableName);

                let! exists = cloudTable.ExistsAsync() |> Async.AwaitTask

                if exists 
                then return Ok <| Some cloudTable
                else return Ok <| None

            with ex -> return Error <| ex.GetBaseException().Message

        } |> Async.StartAsTask

    let ensureExists (ConnectionString connectionString) (Table tableName) =

        async {

            try
                let storageAccount   = CloudStorageAccount.Parse connectionString
                let cloudTableClient = storageAccount.CreateCloudTableClient()
                let cloudTable       = cloudTableClient.GetTableReference(tableName);

                let! exists = cloudTable.ExistsAsync() |> Async.AwaitTask

                if not exists then
        
                    let! success = cloudTable.CreateIfNotExistsAsync() |> Async.AwaitTask

                    if   success
                    then return Ok cloudTable
                    else return Error <| sprintf "Failed to create: %s" tableName

                else return Ok cloudTable
                    

            with
                ex -> return Error <| sprintf "%s\n\n%s" (ex.GetBaseException().Message) ex.StackTrace

        } |> Async.StartAsTask
    
    let findEntity<'T when 'T :> TableEntity> (PartitionKey partitionKey) (RowKey rowKey) (storageTable:CloudTable) =

        async {
            
            let  operation      = TableOperation.Retrieve<'T>(partitionKey, rowKey)
            let! retrieveResult = storageTable.ExecuteAsync(operation) |> Async.AwaitTask

            if retrieveResult.Result <> null then
                 return Some (retrieveResult.Result :?> 'T)
            else return None

        } |> Async.StartAsTask

    let getEntitiesAsync<'T when 'T : (new : unit -> 'T :> TableEntity)> (storageTable:CloudTable) =

        async {
        
            try

                let    query = TableQuery<'T>()
                let!   task  = storageTable.ExecuteQuerySegmentedAsync(query,null) |> Async.AwaitTask

                return Ok task.Results

            with ex -> return Error <| ex.GetBaseException().Message

        } |> Async.StartAsTask

    let updateEntity (cloudTable:CloudTable) (entity:TableEntity) = 

        async {

            let pKey,rKey = PartitionKey entity.PartitionKey, RowKey entity.RowKey

            match! cloudTable |> findEntity pKey rKey |> Async.AwaitTask with
            | None   -> return Error <| sprintf "Entity not found: %A" entity
            | Some _ ->

                let  operation = TableOperation.Replace(entity)
                let! result    = cloudTable.ExecuteAsync(operation) |> Async.AwaitTask

                if result.HttpStatusCode = (int)HttpStatusCode.NoContent
                then return Ok    <| (result.Result :?> TableEntity)
                else return Error <| sprintf "%i: Failed to update entity" result.HttpStatusCode
        }

    let create (entity:TableEntity) (connectionstring:ConnectionString) (tableName:string) =

        async {
        
            let table = Table tableName

            match! table |> ensureExists connectionstring |> Async.AwaitTask with
            | Error msg     -> return Error msg
            | Ok cloudTable ->

                let  operation = TableOperation.Insert(entity)
                let! result    = cloudTable.ExecuteAsync(operation) |> Async.AwaitTask
                
                if result.HttpStatusCode = (int)HttpStatusCode.NoContent
                then return Ok ()
                else return Error <| result.HttpStatusCode.ToString()
        }

    let deleteEntity<'T when 'T :> TableEntity> (PartitionKey partitionKey) (RowKey rowKey) (cloudTable:CloudTable) =

        async {
        
            let  operation      = TableOperation.Retrieve<'T>(partitionKey, rowKey)
            let! retrieveResult = cloudTable.ExecuteAsync(operation) |> Async.AwaitTask

            if retrieveResult.Result <> null then

                 let  entity    = retrieveResult.Result :?> 'T
                 let  operation = TableOperation.Delete entity
                 let! result    = cloudTable.ExecuteAsync(operation) |> Async.AwaitTask

                 if result.HttpStatusCode = (int)HttpStatusCode.NoContent
                 then return Ok ()
                 else return Error <| sprintf "%i: Failed to delete entity" result.HttpStatusCode
        
            else 
                 return Error <| sprintf "Failed to delete %s::%s" partitionKey rowKey

        } |> Async.StartAsTask

    let deleteEntities<'T when 'T :> TableEntity> (PartitionKey partitionKey) (cloudTable:CloudTable) =

        async {
            
            try
                let batchOperation = TableBatchOperation()
                let filter         = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey)
                            
                let  rowKeyItem = System.Collections.Generic.List<string>(seq ["RowKey"])
                let  query      = TableQuery<DynamicTableEntity>().Where(filter).Select(rowKeyItem)
                let! result     = cloudTable.ExecuteQuerySegmentedAsync<_>(query, null) |> Async.AwaitTask

                for item in result  do
                    batchOperation.Delete item
        
                if batchOperation.Count = 0
                then return Ok ()
                else 
                    do! cloudTable.ExecuteBatchAsync(batchOperation) |> Async.AwaitTask |> Async.Ignore
                    return Ok ()

            with ex -> return Error <| ex.GetBaseException().Message

        } |> Async.StartAsTask