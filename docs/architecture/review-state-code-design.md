# Review-state code design

The private booking-review GET endpoint is the first incremental backend design refactor.

- CustomerReviewEndpoints handles routing, authenticated identity, HTTP status codes, and existing CSRF/cache filters.
- BookingReviewStateService is a scoped query service. It filters booking access in SQL, projects only required fields, and returns explicit response records.
- ReviewEligibility contains pure, database-independent display-eligibility rules with focused unit tests. Ordered guard clauses preserve existing reason precedence.
- Response records preserve the existing camel-case JSON contract and exclude private actor and organization identifiers.

The query service uses two sequential database queries instead of five. It propagates request cancellation; it does not run concurrent operations on the same EF DbContext. Missing or inaccessible bookings still return 404, and a missing authenticated account claim returns 401.

These are display rules, not a replacement for submission authorization. Review/reply POST endpoints still re-check permissions, validation, and database uniqueness at write time. This change does not alter persistence, migrations, or frontend contracts.

No generic repository, interface-only wrapper, or additional architecture framework is needed for this query. Extend this pattern incrementally to submission use cases and other endpoint groups, keeping their security and transaction regression tests.
