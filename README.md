# AdGuardHome Querylog Exporter

A simple .NET worker service that exports AdGuardHome query logs to Elasticsearch.

## Getting Started

### Prerequisites

- Docker
- Docker Compose
- Elasticsearch

### Build

1. Clone the repository
1. Run `docker-compose build`

#### Multi-arch build

```bash
docker buildx build --platform linux/arm64,linux/amd64 \
	-t registry.brotal.net/agh-elastic-exporter:latest \
	-f ./src/Dockerfile .
```

### Deployment

1. Copy the `docker-compose.yml` file to your server
1. Create a directory in the same directory as the `docker-compose.yml` file called `work`
	```bash
	mkdir work
	```
	> this directory will be used to keep track of the last processed query log
1. Create a directory in the same directory as the `docker-compose.yml` file called `conf`
	```bash
	mkdir conf
	```
	> this directory will be used to store the `appsettings.json` file
1. Copy the `appsettings.json` file to the `conf` directory`
1. Update the `appsettings.json` file with the correct Config values (Elasticsearch endpoints, user credentials, indexName, etc).
1. Update the instance name of your `AdGuardHome` instance in the `appsettings.json` file. 
1. Update the compose file with correct volume bindings (appsettings.json, logs to be exported, AGH config, and the `work` directory)
1. Run `docker-compose up -d`
