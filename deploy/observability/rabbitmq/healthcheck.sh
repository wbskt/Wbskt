#!/bin/sh
# The broker's healthcheck, which also keeps the callback queue bounded. The hosts wait for this
# check to pass before they start, so the policy is in place before anything sends to the queue.
#
# workflow-inbound-callback is fed by the anonymous public webhook and wake endpoints. With the engine
# down it would otherwise grow until RabbitMQ's memory or disk alarm blocks every publisher, device
# traffic included. The policy caps it, and reject-publish makes the broker refuse what is over the cap,
# so management answers the caller 503 (retry later) instead of 202. Nothing expires: a callback the
# caller was told was accepted runs once the engine is back.
#
# A policy rather than queue arguments, because the queue already exists in production without them
# and RabbitMQ refuses to redeclare a queue with different arguments. It is set only when missing or
# different, so the broker log does not record it every ten seconds.
set -eu

rabbitmq-diagnostics -q ping

name=inbound-callback-bound
pattern='^workflow-inbound-callback$'
# 10,000 callbacks or 64 MiB, whichever comes first. Keys in the order list_policies prints them.
definition='{"max-length":10000,"max-length-bytes":67108864,"overflow":"reject-publish"}'
priority=10

expected="$(printf '/\t%s\t%s\tqueues\t%s\t%s' "$name" "$pattern" "$definition" "$priority")"
if ! rabbitmqctl -q list_policies --no-table-headers | grep -qxF "$expected"; then
    rabbitmqctl -q set_policy --apply-to queues --priority "$priority" "$name" "$pattern" "$definition"
fi
