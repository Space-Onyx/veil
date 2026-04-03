using Content.Shared.Cargo.Prototypes;
using Content.Shared.Mind;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Economy;

public sealed class BankAccount
{
    private const int MaxTransactions = 1000;
    private readonly Queue<TransactionRecord> _transactions = new();

    public void AddTransaction(TransactionRecord record)
    {
        if (_transactions.Count >= MaxTransactions)
            _transactions.Dequeue();
        _transactions.Enqueue(record);
    }

    public List<TransactionRecord> GetTransactions(int count = 1000)
    {
        if (count <= 0)
            return new List<TransactionRecord>();

        if (count > MaxTransactions)
            count = MaxTransactions;

        var transactionCount = _transactions.Count;
        if (transactionCount == 0)
            return new List<TransactionRecord>();

        if (count > transactionCount)
            count = transactionCount;

        var ordered = _transactions.ToArray();
        var result = new List<TransactionRecord>(count);
        for (var i = transactionCount - 1; i >= transactionCount - count; i--)
        {
            result.Add(ordered[i]);
        }

        return result;
    }

    public readonly int AccountId;
    public int AccountPin;
    public int Balance;
    public bool CommandBudgetAccount;
    public Entity<MindComponent>? Mind;
    public string Name = string.Empty;
    public ProtoId<CargoAccountPrototype>? AccountPrototype;
    public EntityUid? CartridgeUid;

    public BankAccount(int accountId, int balance, IRobustRandom random)
    {
        AccountId = accountId;
        Balance = balance;
        AccountPin = random.Next(1000, 10000);
    }
}

