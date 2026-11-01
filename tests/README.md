# Tests

Use this folder for automated test projects and supporting test assets, including unit, integration, and end-to-end tests.

Mirror the structure of the projects under test, and keep tests deterministic, isolated, and repeatable.

The Database, Api, and AppHost tests start real PostgreSQL and Azurite containers (Testcontainers / Aspire testing), so **Docker must be running** to run them. Each test class works in its own empty database; the database is never mocked.
