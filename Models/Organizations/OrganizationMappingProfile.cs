using AutoMapper;
using SmartInventory.Models.Organizations;

namespace SmartInventory.Models.Structure;

public class OrganizationMappingProfile : Profile
{
    public OrganizationMappingProfile()
    {
        CreateMap<Organization, OrganizationEditViewModel>().ReverseMap();
        
        CreateMap<Restaurant, RestaurantEditViewModel>().ReverseMap();
        CreateMap<AccessCode, AccessCodeEditViewModel>().ReverseMap();
        
        CreateMap<Organization, OrganizationIndexViewModel>()
            .ForMember(dest => dest.RestaurantsCount, 
                opt => opt.MapFrom(src => src.Restaurants.Count))
            .ForMember(dest => dest.AccessCodesCount,
                opt => opt.MapFrom(src => src.AccessCodes.Count));
    }
}