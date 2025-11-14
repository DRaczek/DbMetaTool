SET TERM ^ ;
DECLARE VARIABLE FN VARCHAR(50);
    DECLARE VARIABLE LN VARCHAR(50);
    DECLARE VARIABLE HD D_HIRE_DATE;
    DECLARE VARIABLE SAL D_PRICE;
BEGIN
   FOR SELECT FirstName, LastName, HireDate, Salary
        FROM EMPLOYEES
        WHERE EmployeeID = :EMP_ID
        INTO :FN, :LN, :HD, :SAL
    DO
    BEGIN
        FULL_NAME = LN || ', ' || FN;
        HIRE_DATE_INFO = 'Zatrudniony od: ' || HD;
        SALARY_INFO = 'Pensja: ' || SAL || ' PLN';
        SUSPEND;
    END
END
SET TERM ; ^