﻿namespace FSharp.Data

open System
open System.Data
open System.Collections.Generic
open Npgsql

module internal CompilerMessage = 
    [<Literal>]
    let infrastructure = "This API supports the FSharp.Data.Npgsql infrastructure and is not intended to be used directly from your code."

[<Sealed>]
[<CompilerMessageAttribute(CompilerMessage.infrastructure, 101, IsHidden = true)>]
type DataTable<'T when 'T :> DataRow>(selectCommand: NpgsqlCommand) as this = 
    inherit DataTable() 

    let update (tx, conn, batchSize, continueUpdateOnError, conflictOption)  = 
        selectCommand.Transaction <- tx
        selectCommand.Connection <- conn
        
        use dataAdapter = new NpgsqlDataAdapter(selectCommand)

        use commandBuilder = new NpgsqlCommandBuilder(dataAdapter)
        commandBuilder.ConflictOption <- defaultArg conflictOption ConflictOption.OverwriteChanges

        use __ = dataAdapter.RowUpdating.Subscribe(fun args ->

            if  args.Errors = null 
                && args.StatementType = Data.StatementType.Insert 
                && defaultArg batchSize dataAdapter.UpdateBatchSize = 1
            then 
                let columnsToRefresh = ResizeArray()
                for c in this.Columns do
                    if c.AutoIncrement  
                        || (c.AllowDBNull && args.Row.IsNull c.Ordinal)
                    then 
                        columnsToRefresh.Add( commandBuilder.QuoteIdentifier c.ColumnName)

                if columnsToRefresh.Count > 0
                then                        
                    let returningClause = columnsToRefresh |> String.concat "," |> sprintf " RETURNING %s"
                    let cmd = args.Command
                    cmd.CommandText <- cmd.CommandText + returningClause
                    cmd.UpdatedRowSource <- UpdateRowSource.FirstReturnedRecord
        )

        batchSize |> Option.iter dataAdapter.set_UpdateBatchSize
        continueUpdateOnError |> Option.iter dataAdapter.set_ContinueUpdateOnError

        dataAdapter.Update(this)        

    member this.Update(transaction, ?batchSize, ?continueUpdateOnError, ?conflictOption) = 
        update(transaction, transaction.Connection, batchSize, continueUpdateOnError, conflictOption)

    member this.Update(connectionString, ?batchSize, ?continueUpdateOnError, ?conflictOption) = 
        use conn = new NpgsqlConnection(connectionString)
        update(null, conn, batchSize, continueUpdateOnError, conflictOption)

