﻿using GraphQL.Types;

public class ResultGraph :
    ObjectGraphType<Result>
{
    public ResultGraph() =>
        Field(_ => _.Argument);
}