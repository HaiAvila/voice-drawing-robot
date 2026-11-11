public static class NetworkConfig
{
    public static string IP     = "172.22.80.61";
    public static int    Puerto = 5050;
    public static string Url    => $"http://{IP}:{Puerto}";

    // Robot real — actualizar IPRobot cuando tengas la IP
    public static string IPRobot     = "192.168.3.75";
    public static int    PuertoRobot = 5050;
    public static string UrlRobot    => $"http://{IPRobot}:{PuertoRobot}/enviar_rutina";
}