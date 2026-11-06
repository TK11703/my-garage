INSERT INTO reminder_completions (id, reminder_id, done_on, mileage, created_at, created_by)
VALUES (@CompletionId, @ReminderId, @DoneOn, @Mileage, @Now, @OwnerId);
