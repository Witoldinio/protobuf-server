CREATE TABLE ItemStat
(
    ItemStatID SERIAL CONSTRAINT PK_ItemStat PRIMARY KEY,
    ItemID INTEGER NOT NULL CONSTRAINT FK_ItemStat_Item REFERENCES Item,
    StatID INTEGER NOT NULL CONSTRAINT FK_ItemStat_Stat REFERENCES Stat,
    StatValue REAL NOT NULL
);