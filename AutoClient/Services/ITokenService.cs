using AutoClient.Models;

namespace AutoClient.Services;

public interface ITokenService
{
    string CreateToken(Workshop workshop);
}
