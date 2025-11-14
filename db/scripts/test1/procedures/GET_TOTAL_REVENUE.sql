SET TERM ^ ;
BEGIN
    SELECT SUM(oi.Quantity * p.UnitPrice * (1 - oi.Discount))
    FROM ORDER_ITEMS oi
    JOIN PRODUCTS p ON oi.ProductID = p.ProductID
    INTO :TOTAL_REVENUE;
    SUSPEND;
END
SET TERM ; ^