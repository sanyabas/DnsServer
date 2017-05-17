# Caching DNS server

Receives DNS packets on 53/UDP port, searches query in the cache, if miss, resolves it by requesting specified DNS server (` 8.8.8.8 ` by default).
Cache is being serialized on exit and loaded from file on startup.

## Arguments

` -s ` Remote DNS server to handle queries

` -f ` Path to cache file
