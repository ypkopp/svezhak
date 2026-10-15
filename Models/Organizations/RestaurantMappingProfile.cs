using AutoMapper;
using SmartInventory.Models.Restaurants;

namespace SmartInventory.Models.Structure;

public class RestaurantMappingProfile : Profile
{
    public RestaurantMappingProfile()
    {
        CreateMap<Restaurant, RestaurantIndexViewModel>()
            .ForMember(dest => dest.OrganizationName,
                opt => opt.MapFrom(src => src.Organization.Name));

        CreateMap<Restaurant, RestaurantEditViewModel>()
            .ReverseMap();
    }
}