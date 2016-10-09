/**
* CRL 快速开发框架 V4.0
* Copyright (c) 2016 Hubro All rights reserved.
* GitHub https://github.com/hubro-xx/CRL3
* 主页 http://www.cnblogs.com/hubro
* 在线文档 http://crl.changqidongli.com/
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using CoreHelper;
using MongoDB.Driver;
using MongoDB.Bson;
namespace CRL.LambdaQuery
{
    public abstract class LambdaQueryBase
    {
        /// <summary>
        /// 当前类型
        /// </summary>
        protected Type __MainType;
        internal ExpressionVisitor __Visitor;
        /// <summary>
        /// 查询字段是否需要加上前辍,如t1.Id
        /// </summary>
        internal bool __UseTableAliasesName = true;

        /// <summary>
        /// 查询的字段
        /// </summary>
        internal List<Attribute.FieldAttribute> __QueryFields = new List<CRL.Attribute.FieldAttribute>();
        /// <summary>
        /// group字段
        /// </summary>
        internal List<Attribute.FieldAttribute> __GroupFields = new List<CRL.Attribute.FieldAttribute>();
        internal Dictionary<Type, string> __Relations = new Dictionary<Type, string>();
        internal DbContext __DbContext;
        internal DBAdapter.DBAdapterBase __DBAdapter;
        /// <summary>
        /// 排序
        /// </summary>
        internal string __QueryOrderBy = "";
        internal Dictionary<Type, JoinType> __JoinTypes = new Dictionary<Type, JoinType>();
        #region 解析选择的字段
        /// <summary>
        /// 解析选择的字段
        /// </summary>
        /// <param name="expressionBody"></param>
        /// <param name="withTablePrefix">是否生按表生成前辍,关联时用 如Table__Name</param>
        /// <param name="types"></param>
        /// <returns></returns>
        internal List<Attribute.FieldAttribute> GetSelectField(bool isSelect,Expression expressionBody, bool withTablePrefix, params Type[] types)
        {
            var allFilds = new Dictionary<Type, IgnoreCaseDictionary<Attribute.FieldAttribute>>();
            //var mainType = typeof(T);
            allFilds.Add(__MainType, TypeCache.GetProperties(__MainType, true));
            foreach (var t in types)
            {
                if (!allFilds.ContainsKey(t))
                {
                    allFilds.Add(t, TypeCache.GetProperties(t, true));
                }
            }
            List<Attribute.FieldAttribute> resultFields = new List<Attribute.FieldAttribute>();

            if (expressionBody is NewExpression)//按匿名对象
            {
                #region 按匿名对象
                var newExpression = expressionBody as NewExpression;
                int i = 0;
                foreach (var item in newExpression.Arguments)
                {
                    var memberName = newExpression.Members[i].Name;
                    if (item is MethodCallExpression)//group用
                    {
                        var methodCallExpression = item as MethodCallExpression;
                        string methodMember;
                        var methodQuery = getSelectMethodCall(methodCallExpression, out methodMember);
                        var f = allFilds[__MainType].First().Value.Clone();
                        //f.QueryFullName = methodQuery + " as " + memberName;
                        f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, memberName, methodQuery);
                        f.FieldQuery = new Attribute.FieldQuery() { MemberName = memberName, FieldName = methodMember, MethodName = methodCallExpression.Method.Name };
                        resultFields.Add(f);
                    }
                    else if (item is BinaryExpression)
                    {
                        var field = getSeletctBinary(item);
                        var f = allFilds[__MainType].First().Value.Clone();
                        //f.QueryFullName = string.Format("{0} as {1}", field, memberName);
                        f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, memberName, field);
                        f.FieldQuery = new Attribute.FieldQuery() { MemberName = memberName, FieldName = field, MethodName = "" };
                        resultFields.Add(f);
                    }
                    else if (item is ConstantExpression)//常量
                    {
                        var constantExpression = item as ConstantExpression;
                        var f = allFilds[__MainType].First().Value.Clone();
                        var value = constantExpression.Value + "";
                        if (!value.IsNumber())
                        {
                            value = string.Format("'{0}'", value);
                        }
                        //f.QueryFullName = string.Format("{0} as {1}", value, memberName);
                        f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, memberName, value);
                        f.FieldQuery = new Attribute.FieldQuery() { MemberName = memberName, FieldName = value, MethodName = "" };
                        resultFields.Add(f);
                    }
                    else if (item is MemberExpression)
                    {
                        var memberExpression = item as MemberExpression;//转换为属性访问表达式
                        var f = allFilds[memberExpression.Expression.Type][memberExpression.Member.Name];
                        if (memberName != memberExpression.Member.Name)//按有别名算
                        {
                            //f.QueryFullName = string.Format("t1.{0} as {1}", f.Name, memberName);
                            f.SetFieldQueryScript2(__DBAdapter, true, withTablePrefix, memberName);
                        }
                        else
                        {
                            //var aliasName = GetPrefix(memberExpression.Expression.Type);
                            //f.SetFieldQueryScript(aliasName, true, false);
                            //字段名和属性名不一样时才生成别名
                            //todo 属性别名不一样时,查询应返回属性名
                            string fieldName = "";
                            if (isSelect)//查询字段时按属性名生成别名
                            {
                                if (!string.IsNullOrEmpty(f.MappingName))
                                {
                                    fieldName = f.MemberName;
                                }
                                //if (withTablePrefix)
                                //{
                                //    fieldName = f.MappingName != f.MemberName ? f.MemberName : "";
                                //}
                                //else
                                //{
                                //    if (!string.IsNullOrEmpty(f.MappingName))
                                //    {
                                //        fieldName = f.MemberName;
                                //    }
                                //}
                            }
                            f.SetFieldQueryScript2(__DBAdapter, true, withTablePrefix, fieldName);
                        }
                        f.FieldQuery = new Attribute.FieldQuery() { MemberName = memberName, FieldName = f.MappingName, MethodName = "" };
                        resultFields.Add(f);
                    }
                    else
                    {
                        throw new Exception("不支持此语法解析:" + item);
                    }
                    i += 1;
                }
                #endregion
            }
            else if (expressionBody is MethodCallExpression)
            {
                #region 方法
                var method = expressionBody as MethodCallExpression;
                var f = allFilds[__MainType].First().Value.Clone();
                string methodMember;
                var methodQuery = getSelectMethodCall(expressionBody, out methodMember);
                //f.QueryFullName = methodQuery;
                f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, "", methodQuery);
                f.FieldQuery = new Attribute.FieldQuery() { MemberName = methodMember, FieldName = methodMember, MethodName = method.Method.Name };
                resultFields.Add(f);
                #endregion
            }
            else if (expressionBody is BinaryExpression)
            {
                var field = getSeletctBinary(expressionBody);
                var f = allFilds[__MainType].First().Value.Clone();
                //f.QueryFullName = string.Format("{0}", field);
                f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, "", field);
                f.FieldQuery = new Attribute.FieldQuery() { MemberName = f.MemberName, FieldName = field, MethodName = "" };
                resultFields.Add(f);
            }
            else if (expressionBody is ConstantExpression)
            {
                var constant = (ConstantExpression)expressionBody;
                var f = allFilds[__MainType].First().Value.Clone();
                //f.QueryFullName = string.Format("{0}", constant.Value);
                f.SetFieldQueryScript2(__DBAdapter, false, withTablePrefix, "", constant.Value + "");
                f.FieldQuery = new Attribute.FieldQuery() { MemberName = f.MemberName, FieldName = constant.Value + "", MethodName = "" };
                resultFields.Add(f);
            }
            else if (expressionBody is UnaryExpression)
            {
                var unaryExpression = expressionBody as UnaryExpression;
                return GetSelectField(false,unaryExpression.Operand, withTablePrefix, types);
            }
            else if (expressionBody is MemberExpression)//按成员
            {
                MemberExpression mExp;
                //if (expressionBody is UnaryExpression)//当被CONVET()运算
                //{
                //    var exp2 = expressionBody as UnaryExpression;
                //    mExp = exp2.Operand as MemberExpression;
                //}
                //else
                //{
                //    mExp = (MemberExpression)expressionBody;
                //}
                mExp = (MemberExpression)expressionBody;
                var aliasName = GetPrefix(mExp.Expression.Type);
                var f = allFilds[mExp.Expression.Type][mExp.Member.Name].Clone();
                //f.SetFieldQueryScript(aliasName, true, false);
                f.SetFieldQueryScript2(__DBAdapter, true, withTablePrefix, "");
                f.FieldQuery = new Attribute.FieldQuery() { MemberName = f.MemberName, FieldName = f.MappingName, MethodName = "" };
                resultFields.Add(f);
            }
            else
            {
                throw new Exception("不支持此语法解析:" + expressionBody);
            }
            return resultFields;
        }
        /// <summary>
        /// 返回方法调用拼接
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        string getSelectMethodCall(Expression expression,out string memberName)
        {
            var method = expression as MethodCallExpression;
            MemberExpression memberExpression;
            var args = method.Arguments[0];
            memberName = "";
            string methodArgs = "";
            if (args is ParameterExpression)
            {
                var exp2 = method.Arguments[1] as UnaryExpression;
                var type = exp2.Operand.GetType();
                var p = type.GetProperty("Body");
                var exp3 = p.GetValue(exp2.Operand, null) as Expression;
                methodArgs = getSeletctBinary(exp3);
                memberName = "";
            }
            else if (args is UnaryExpression)//like a.Code.Count()
            {
                memberExpression = (args as UnaryExpression).Operand as MemberExpression;
                memberName = memberExpression.Member.Name;
                methodArgs = GetPrefix(memberExpression.Expression.Type) + memberExpression.Member.Name;
            }
            else if (args is MemberExpression)
            {
                //like a.Code
                memberExpression = args as MemberExpression;
                memberName = memberExpression.Member.Name;
                methodArgs = GetPrefix(memberExpression.Expression.Type) + memberExpression.Member.Name;
            }
            else
            {
                throw new Exception("不支持此语法解析:" + args);
            }
            string methodName = method.Method.Name;

            return string.Format("{0}({1})", methodName, methodArgs);
        }
        /// <summary>
        /// 返回二元运算调用
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        string getSeletctBinary(Expression expression)
        {
            var str = __Visitor.RouteExpressionHandler(expression).SqlOut;
            return str;
        }
        #endregion

        #region 别名
        /// <summary>
        /// 别名
        /// </summary>
        internal Dictionary<Type, string> __Prefixs = new Dictionary<Type, string>();
        int prefixIndex = 0;
        /// <summary>
        /// 获取别名,如t1.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal string GetPrefix(Type type = null)
        {
            if (type == null)
            {
                type = __MainType;
            }

            if (!__Prefixs.ContainsKey(type))
            {
                prefixIndex += 1;
                string str = string.Format("t{0}.", prefixIndex);
                if (!__UseTableAliasesName)
                {
                    str = "";
                }

                __Prefixs[type] = str;
            }
            return __Prefixs[type];
        }
        /// <summary>
        /// 替换别名
        /// </summary>
        /// <param name="condition"></param>
        /// <returns></returns>
        internal string ReplacePrefix(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return condition;
            }
            foreach (var type in __Prefixs.Keys)
            {
                //将完整名称替换成别名
                condition = condition.Replace("{" + type + "}", GetPrefix(type));
            }
            return condition;
        }
        #endregion
        internal void SelectAll()
        {
            //var all = TypeCache.GetProperties(__MainType, false).Values;
            var all = TypeCache.GetQueryProperties(__MainType);
            __QueryFields.Clear();
            var aliasName = GetPrefix();
            foreach (var item in all)
            {
                //item.SetFieldQueryScript(aliasName, true, false);
                item.SetFieldQueryScript2(__DBAdapter, true, false, "");
                //__QueryFields.Add(item);
            }
            __QueryFields = new List<Attribute.FieldAttribute>(all);
        }

        /// <summary>
        /// 转换为SQL条件，并提取参数
        /// </summary>
        /// <param name="expressionBody"></param>
        /// <returns></returns>
        internal CRLExpression.CRLExpression FormatExpression(Expression expressionBody)
        {
            //string condition;
            if (expressionBody == null)
                return null;
            var result = __Visitor.RouteExpressionHandler(expressionBody);
            if (string.IsNullOrEmpty(result.SqlOut))//没有构成树
            {
                string typeStr2 = "";
                result.SqlOut = __Visitor.DealParame(result, "", out typeStr2).Data + "";
            }
            result.SqlOut = ReplacePrefix(result.SqlOut);
            //RouteCRLExpression(result);
            return result;
            //condition = __Visitor.RouteExpressionHandler(expressionBody).FullOut;
            //condition = ReplacePrefix(condition);
            //return condition;
        }
        
        internal string FormatJoinExpression(Expression expressionBody)
        {
            string condition;
            if (expressionBody == null)
                return "";
            condition = __Visitor.RouteExpressionHandler(expressionBody).SqlOut;
            //GetPrefix(typeof(TInner));
            condition = ReplacePrefix(condition);
            return condition;
        }
        internal void AddInnerRelationCondition(Type inner, string condition)
        {
            __Relations[inner] += "  and " + condition;
        }
        internal void AddInnerRelation(Type inner, string condition)
        {
            if (__Relations.ContainsKey(inner))
            {
                throw new Exception(string.Format("关联查询已包含关联对象 {0} {1}", inner,condition));
                return;
            }
            if (__MainType == inner)
            {
                throw new Exception(string.Format("关联查询不能指定自已 {0} {1}" , inner,condition));
                return;
            }
            DBExtendFactory.CreateDBExtend(__DbContext).CheckTableCreated(inner);
            var tableName = TypeCache.GetTableName(inner, __DbContext);

            string aliasName = GetPrefix(inner);
            var joinType = __JoinTypes[inner];
            tableName = string.Format("{0} {1} ", __DBAdapter.KeyWordFormat(tableName), aliasName.Substring(0, aliasName.Length - 1));
            string str = string.Format(" {0} join {1} on {2}", joinType, tableName + " " + __DBAdapter.GetWithNolockFormat(),
               condition);
            if (!__Relations.ContainsKey(inner))
            {
                __Relations.Add(inner, str);
            }
        }
    }
}
