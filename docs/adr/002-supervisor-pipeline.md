# ADR 002: Supervisor pipeline

Use a supervisor-controlled sequential pipeline. It gives each specialist a restricted tool set, deterministic ordering, bounded retries, a timeout and an inspectable trace. A free-form agent swarm was rejected because it makes approval and failure analysis harder.
