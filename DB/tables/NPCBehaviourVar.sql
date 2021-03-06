CREATE TABLE NPCBehaviourVar
(
    NPCBehaviourVarID SERIAL CONSTRAINT PK_NPCBehaviourVar PRIMARY KEY,
    NPCBehaviourID INTEGER CONSTRAINT FK_NPCBehaviourVar_NPCBehaviour REFERENCES NPCBehaviour,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL
);