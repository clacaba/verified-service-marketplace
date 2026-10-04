# Review-state code design

The private booking-review GET endpoint is the first incremental backend design refactor.

- CustomerReviewEndpoints handles routing, authenticated identity, HTTP status codes, and existing CSRF/cache filters.
- BookingReviewStateService is a scoped query service. It filters booking access in SQL, projects only required fields, and returns explicit response records.
- ReviewEligibility contains pure, database-independent display-eligibility rules with focused unit tests. Ordered guard clauses preserve existing reason precedence.
- Response records preserve the existing camel-case JSON contract and exclude private actor and organization identifiers.

The query service uses two sequential database queries instead of five. It propagates request cancellation; it does not run concurrent operations on the same EF DbContext. Missing or inaccessible bookings still return 404, and a missing authenticated account claim returns 401.

These are display rules, not a replacement for submission authorization. Review/reply POST endpoints still re-check permissions, validation, and database uniqueness at write time. This change does not alter persistence, migrations, or frontend contracts.

No generic repository, interface-only wrapper, or additional architecture framework is needed for this query.

## Submission use cases

ReviewSubmissionService and ProviderReplySubmissionService now own the two write use cases. They return typed, transport-independent SubmissionResult outcomes; endpoints translate these to the existing 201/400/403/404/409 HTTP responses.

ReviewSubmissionValidator and ProviderReplySubmissionValidator share feedback rules without supplying an artificial rating for replies. Authorization and state guards still precede payload validation in their original order. Display eligibility is never trusted to authorize a write.

Every EF query and save receives request cancellation. Database keys still enforce one review/reply per booking; duplicate insert races remain conflicts. The reply and review.replied audit event are saved together in one EF SaveChanges transaction. Unexpected persistence failures are rethrown rather than disguised as successful submissions.

Existing HTTP regression tests cover ownership, email verification, CSRF, hidden reviews, duplicate races, and audit atomicity. Dedicated validator tests cover rating boundaries, trimmed comment length, forbidden control characters, Unicode UTF-16 length, and public consent. No schema or frontend change is required.
