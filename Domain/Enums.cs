namespace TruckMate.Domain;

public enum TransactionStatus
{
    PENDING,
    COMPLETED,
    NOT_COMPLETED
}

public enum TransactionType
{
    SUPPLY,
    WAREHOUSE
}

public enum Direction
{
    IN,
    OUT
}

public enum TicketOutputType
{
    PRINT,
    PDF
}

public enum MasterItemType
{
    SUPPLIER,
    TRANSPORTER,
    DRIVER,
    SUBTYPE
}

public enum OperatorRole
{
    Operator,
    Admin,
    SuperAdmin
}
