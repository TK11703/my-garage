SELECT pg_advisory_xact_lock(hashtext(@OwnerId || ':' || @Kind));
