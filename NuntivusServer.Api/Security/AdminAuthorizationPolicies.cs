namespace NuntivusServer.Api.Security
{
    public static class AdminAuthorizationPolicies
    {
        public const string Viewer = "admin.viewer";
        public const string Operator = "admin.operator";
        public const string Admin = "admin.admin";

        public const string ViewerRole = "Viewer";
        public const string OperatorRole = "Operator";
        public const string AdminRole = "Admin";
    }
}
