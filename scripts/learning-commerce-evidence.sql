-- Read only. Run only against the dedicated database created by the Week 6 gate.
SET NOCOUNT ON;

SELECT   c.Id AS CourseId,
         c.Title,
         c.Status,
         c.IsPaid,
         c.Price,
         (SELECT COUNT(*)
          FROM   __EFMigrationsHistory) AS AppliedMigrations,
         (SELECT COUNT(*)
          FROM   AspNetUsers
          WHERE  Email LIKE '%@demo.example.test') AS DemoUsers,
         (SELECT COUNT(*)
          FROM   [Order] AS o
          WHERE  o.CourseId = c.Id) AS Orders,
         (SELECT COUNT(*)
          FROM   Payment AS p
                 INNER JOIN
                 [Order] AS o
                 ON o.Id = p.OrderId
          WHERE  o.CourseId = c.Id
                 AND p.Status = 'Failed') AS FailedPayments,
         (SELECT COUNT(*)
          FROM   Payment AS p
                 INNER JOIN
                 [Order] AS o
                 ON o.Id = p.OrderId
          WHERE  o.CourseId = c.Id
                 AND p.Status = 'Succeeded') AS SuccessfulPayments,
         (SELECT COALESCE (SUM(p.Amount), 0)
          FROM   Payment AS p
                 INNER JOIN
                 [Order] AS o
                 ON o.Id = p.OrderId
          WHERE  o.CourseId = c.Id
                 AND p.Status = 'Succeeded'
                 AND p.Amount = o.Amount
                 AND o.Currency = 'VND') AS SuccessfulPaymentAmount,
         (SELECT COUNT(*)
          FROM   Enrollments AS e
          WHERE  e.CourseId = c.Id) AS Enrollments,
         (SELECT COUNT(*)
          FROM   Enrollments AS e
          WHERE  e.CourseId = c.Id
                 AND e.CompletedAtUtc IS NOT NULL) AS HistoricalCompletions,
         (SELECT COUNT(*)
          FROM   Lessons AS l
                 INNER JOIN
                 Modules AS m
                 ON m.Id = l.ModuleId
          WHERE  m.CourseId = c.Id
                 AND m.Visibility = 'Visible'
                 AND l.Status = 'Published') AS EffectiveLessons,
         (SELECT COUNT(*)
          FROM   LessonProgress AS p
                 INNER JOIN
                 Enrollments AS e
                 ON e.Id = p.EnrollmentId
                 INNER JOIN
                 Lessons AS l
                 ON l.Id = p.LessonId
                 INNER JOIN
                 Modules AS m
                 ON m.Id = l.ModuleId
          WHERE  e.CourseId = c.Id
                 AND m.CourseId = c.Id
                 AND m.Visibility = 'Visible'
                 AND l.Status = 'Published'
                 AND p.IsCompleted = 1) AS CompletedEffectiveLessons
FROM     Courses AS c
WHERE    c.Title IN (N'Week 6 UAT · Free Learning Commerce', N'Week 6 UAT · Paid Learning Commerce')
ORDER BY c.IsPaid
FOR      JSON PATH;