# PowerShell Hello World
Write-Output 'Hello PowerShell'

# Retrieve a specific document by id (partition key is /id)
Read-AzCosmosItems -AccountName ztech -DatabaseName Ayan -ContainerName Ayanid -PartitionKey '4cb67ab0-ba1a-0e8a-8dfc-d48472fd5766' `
    -AccountKey $env:ZtechCosmosPrimaryKey

