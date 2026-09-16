# ADR 001: Structural hybrid retrieval

Use structural chunks with section, clause, page and policy-version metadata. Fuse dense pgvector results with keyword retrieval, then filter to the version applicable to the incident date. This reduces incorrect citations from a newer wording.
