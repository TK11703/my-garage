-- Succeeds only when the reminder is still in the state the caller saw, so concurrent evaluators cannot both notify.
UPDATE reminders
SET notified_state = @NewState
WHERE id = @ReminderId
  AND notified_state IS NOT DISTINCT FROM @ExpectedState::text
RETURNING id;
