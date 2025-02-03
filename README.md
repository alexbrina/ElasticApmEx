# ElasticApmEx

```bash
# set git origin user
git remote get-url origin
git remote set-url origin https://alexbrina@github.com/alexbrina/ElasticApmEx.git
```

## Elastic + Kibana + APM

https://www.elastic.co/guide/en/apm/guide/7.17/quick-start-overview.html
https://www.elastic.co/guide/en/apm/agent/dotnet/current/intro.html

```bash

```

## Custom Metrics - Elasticsearch

```bash
PUT _index_template/seller-integration
{
  "index_patterns": ["seller-integration-*"],
  "data_stream": {},
  "priority": 200,
  "template": {
    "settings": {
      "index.codec": "best_compression",
      "number_of_shards": 1,
      "number_of_replicas": 1
    },
    "mappings": {
      "dynamic": "strict",
      "properties": {
        "@timestamp": {
          "type": "date",
          "format": "strict_date_optional_time||epoch_millis"
        },
        "client_id": {
          "type": "keyword"
        },
        "request_metrics": {
          "properties": {
            "total_items": {
              "type": "integer"
            },
            "size_bytes": {
              "type": "long"
            },
            "processing_time_ms": {
              "type": "float"
            }
          }
        },
        "success": {
          "type": "boolean"
        },
        "http_status": {
          "type": "keyword"
        },
        "user_agent": {
          "type": "keyword",
          "ignore_above": 256
        }
      }
    }
  }
}
```

```bash
# Get first 10 docs sorted by timestamp
GET seller-integration/_search
{
  "size": 10,
  "sort": [ { "@timestamp": "desc" } ]
}
```

```http
curl -X GET "http://localhost:9200/seller-integration/_search?pretty" \
    -H 'Content-Type: application/json' \
    -d'
{
  "query": { "match_all": {} },
  "size": 10000
}'
```
