namespace OnlineShopping.Application.Common;

public static class Transactions
{
    /// <summary>
    /// Runs the work in a transaction that is committed only when it succeeds. Inside a transaction that is already open
    /// (a bulk import) the work joins it, and the outer owner decides.
    /// </summary>
    public static async Task<Result<T>> InTransactionAsync<T>(this IApplicationDbContext db, Func<Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);
        if (db.Database.CurrentTransaction is not null)
            return await work();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work();
        if (result.IsSuccess)
            await transaction.CommitAsync(cancellationToken);
        else
            await transaction.RollbackAsync(cancellationToken);
        return result;
    }
}
