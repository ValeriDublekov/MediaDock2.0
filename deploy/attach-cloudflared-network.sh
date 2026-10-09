#!/bin/sh
set -eu

network=mediadock-next-cloudflared
container=mediadock-next-api-1

docker network inspect "$network" >/dev/null
docker inspect "$container" >/dev/null

if docker inspect --format '{{range $name, $network := .NetworkSettings.Networks}}{{printf "%s\n" $name}}{{end}}' "$container" | grep -Fqx -- "$network"; then
	exit 0
fi

docker network connect --alias api "$network" "$container"