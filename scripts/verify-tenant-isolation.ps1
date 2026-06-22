$base='https://ai-ops-web.whitepond-79b93370.centralindia.azurecontainerapps.io'
function Ask($tenant,$q){
  $h=@{'X-Tenant-Id'=$tenant;'Content-Type'='application/json'}
  $b=@{question=$q;topK=3}|ConvertTo-Json
  $r=Invoke-RestMethod -Method Post "$base/api/chat" -Headers $h -Body $b
  '{0,-8} q="{1}" -> [{2}]' -f $tenant,$q,(($r.Citations|ForEach-Object{$_.DocId}) -join ', ')
}
'indexedChunks = ' + (Invoke-RestMethod "$base/api/health").indexedChunks
'acme docs     = ' + ((Invoke-RestMethod "$base/api/documents?tenant=acme").documents.docId -join ', ')
'--- isolation ---'
Ask 'acme'    'How do I restart the ACME widget service?'
Ask 'default' 'How do I restart the ACME widget service?'
Ask 'acme'    'redis cache evictions and connection timeouts'
Ask 'default' 'redis cache evictions and connection timeouts'
